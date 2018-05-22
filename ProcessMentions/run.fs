module ProcessMentions

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open System.Web
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Table
open LinqToTwitter
open Newtonsoft.Json

type Settings =
    { TwitterApiKey : string
      TwitterApiSecret : string
      TwitterAccessToken : string
      TwitterAccessTokenSecret : string} with

    static member load () = 
        { TwitterApiKey =
            Environment.GetEnvironmentVariable("twitterapikey", EnvironmentVariableTarget.Process)
          TwitterApiSecret =
            Environment.GetEnvironmentVariable("twitterapisecret", EnvironmentVariableTarget.Process)
          TwitterAccessToken =
            Environment.GetEnvironmentVariable("twitteraccesstoken", EnvironmentVariableTarget.Process)
          TwitterAccessTokenSecret =
            Environment.GetEnvironmentVariable("twitteraccesstokensecret", EnvironmentVariableTarget.Process)
        }

[<CLIMutable>]
type UserMention =
    { UserID : uint64
      ScreenName : string
      Start : int
      End : int }

[<CLIMutable>]
type Mention =
    { Text : string
      UserMentions : UserMention array
      QuotedTweet : uint64
      InReplyToTweet : uint64
      EmbedHtml : string
      Url : string
      ScreenName : string
      CreatedAt : DateTime
      StatusID : uint64 }

type CodepointRequest =
    | PlainText of text : string
    | Tweet of id : uint64
    | User of id : uint64
    | Blank

module Option =
    let defaultValue x xOpt = match xOpt with Some(y) -> y | None -> x

module CodepointRequest =
    let private trim (s:String) = s.Trim(' ','\r', '\n')
    let private hasUpArrow s = Regex.IsMatch(s, "\\u2B06")
    let private hasDownArrow s = Regex.IsMatch(s, "\\u2B07")
    let private hasRightArrow s = Regex.IsMatch(s, "\\u27A1")
    let private afterRightArrow s =
        match Regex.Match(s, "\\u27A1(?:\\uFE0F)?(.*)", RegexOptions.Singleline) with
        | m when not m.Success -> None
        | m -> m.Groups.[1].Value |> trim |> Some
       
    let analyze (mention : Mention) =
        if mention.InReplyToTweet <> 0uL && hasUpArrow mention.Text then
            Tweet(mention.InReplyToTweet)
        elif mention.QuotedTweet <> 0uL && hasDownArrow mention.Text then
            Tweet(mention.QuotedTweet)
        elif hasRightArrow mention.Text then
            match afterRightArrow mention.Text with
            | None | Some("") -> Blank
            | Some(s) ->
                mention.UserMentions
                |> Array.tryFind (fun um -> (sprintf "@%s" um.ScreenName).ToLower() = s.ToLower())
                |> Option.map (fun um -> User(um.UserID))
                |> Option.defaultValue (PlainText(s))
        else
            Blank

[<CLIMutable>]
 type CodepointInfo =
    { Codepoint : int
      Name : string }

[<CLIMutable>]
type Reply =
    { Mention : Mention

      TargetTweetEmbedHtml : string

      TargetText : string
      Codepoints : CodepointInfo array

      TargetUserScreenName : string
      TargetUserDisplayName : string
      TargetUserSummary : string
      UserScreenNameCodepoints : CodepointInfo array
      UserDisplayNameCodepoints : CodepointInfo array
      UserSummaryCodepoints : CodepointInfo array }

type RequestDataRow() =
   inherit TableEntity()
   member val CodepointJson = null with get,set
   member val TargetScreenNameCodepointJson = null with get,set
   member val TargetDisplayNameCodepointJson = null with get,set
   member val TargetSummaryCodepointJson = null with get,set
   member val MentionText = null with get,set
   member val TargetText = null with get,set
   member val MentionUrl = null with get,set
   member val MentionStatusID = 0uL with get,set
   member val CreatedAt = DateTimeOffset.UtcNow with get,set
   member val MentionEmbedHtml = null with get,set
   member val TargetEmbedHtml = null with get,set
   member val TargetScreenName = null with get,set
   member val TargetDisplayName = null with get,set
   member val TargetSummary = null with get,set

 module Unicode =
    let mutable private store : Dictionary<int, string> = null
    let mutable private ranges : List<(int * int)> = null
    let private parsePatt = """^\<(?<rangeName>[a-zA-Z0-9 ]+?), (?<marker>First|Last)>$"""

    let load rootPath =
        let dataFilePath = Path.Combine(rootPath, "UnicodeData.txt")
        if not (File.Exists(dataFilePath)) then failwithf "Unicode data file does not exist at expected path %O" dataFilePath

        store <- Dictionary<int, string>()
        ranges <- List<(int * int)>()

        let lines = File.ReadLines(dataFilePath)
        let mutable rangeStart = 0

        for line in lines do
            if not (String.IsNullOrEmpty(line)) then
                let fields = line.Split(';')
                let codepoint = Convert.ToInt32(fields.[0], 16)
                match Regex.Match(fields.[1], parsePatt) with
                | m when m.Success ->
                    match m.Groups.["marker"].Value with
                    | "First" ->
                        rangeStart <- codepoint
                    | "Last" ->
                        let rangeName = m.Groups.["rangeName"].Value
                        store.Add(rangeStart, rangeName)
                        ranges.Add((rangeStart, codepoint))
                | _ ->
                    let name = fields.[1]
                    let altName = fields.[10]
                    if name.StartsWith("<") && name.EndsWith(">") && not (String.IsNullOrEmpty(altName)) then
                        store.Add(codepoint, sprintf "%O %O" name altName)
                    else
                        store.Add(codepoint, name)

    let private lookup c =
        if c < 0 || c > 0x10ffff then "INVALID CODEPOINT" else
        match store.TryGetValue(c) with
        | true, n -> n
        | _ ->
            let (rangeStart, _) = ranges |> Seq.find (fun (a, b) -> c >= a && c <= b)
            let n = store.[rangeStart]
            store.[c] <- n
            n

    let codepointInfo (s : string) =
        let mutable i = 0
        let info = List<CodepointInfo>()
        while i < s.Length do
            let codepoint =
                // BMP or paired surrogate
                try Char.ConvertToUtf32(s, i) with
                // unpaired surrogate
                | _ -> int s.[i]

            info.Add({ Codepoint = codepoint
                       Name = lookup codepoint })

            let isPairedHS =
                Char.IsHighSurrogate(s.[i]) && i < (s.Length - 1) && Char.IsLowSurrogate(s.[i+1])

            if isPairedHS then
                i <- i + 2
            else
                i <- i + 1

        info.ToArray()

let Run(mention: Mention,
        replyQueue: ICollector<Mention>,
        requestDataTable: ICollector<RequestDataRow>,
        log: TraceWriter,
        functionContext : ExecutionContext) =
    try
    log.Info(sprintf "Processing mention %O" mention.Url)
    log.Info(sprintf "Text: %O" mention.Text)

    Unicode.load functionContext.FunctionDirectory

    let cpRequest = CodepointRequest.analyze mention
    log.Info(sprintf "Request parsed as %A" cpRequest)

    let settings = Settings.load()
    let context =
        let credentials = SingleUserInMemoryCredentialStore()
        credentials.ConsumerKey <- settings.TwitterApiKey
        credentials.ConsumerSecret <- settings.TwitterApiSecret
        credentials.AccessToken <- settings.TwitterAccessToken
        credentials.AccessTokenSecret <- settings.TwitterAccessTokenSecret
        let authorizer = SingleUserAuthorizer()
        authorizer.CredentialStore <- credentials
        new TwitterContext(authorizer)

    let reply = 
        match cpRequest with
        | Blank -> None
        | PlainText(s) ->
            let codepoints = Unicode.codepointInfo s
            Some { Mention = mention
                   TargetTweetEmbedHtml = null
                   TargetText = s
                   Codepoints = codepoints
                   TargetUserScreenName = null
                   TargetUserDisplayName = null
                   TargetUserSummary = null
                   UserScreenNameCodepoints = null 
                   UserDisplayNameCodepoints = null
                   UserSummaryCodepoints = null }

        | Tweet(id) ->
            let tweet =
                let idList = sprintf "%d" id
                query {
                    for tweet in context.Status do
                    where (tweet.Type = StatusType.Lookup && tweet.TweetIDs = idList && (int tweet.TweetMode) = 1)
                    select tweet
                } |> Seq.head

            let embedInfo =
                query {
                    for tweet in context.Status do
                    where (tweet.Type = StatusType.Oembed && tweet.ID = id && (int tweet.TweetMode) = 1)
                    select tweet.EmbeddedStatus
                } |> Seq.head
            
            let decodedText = HttpUtility.HtmlDecode(tweet.FullText)
            log.Info(sprintf "Getting codepoints for tweet %d: %O" tweet.StatusID decodedText)
            let codepoints = Unicode.codepointInfo decodedText
            Some { Mention = mention
                   TargetTweetEmbedHtml = embedInfo.Html
                   TargetText = decodedText
                   Codepoints = codepoints
                   TargetUserScreenName = null
                   TargetUserDisplayName = null
                   TargetUserSummary = null
                   UserScreenNameCodepoints = null 
                   UserDisplayNameCodepoints = null
                   UserSummaryCodepoints = null }

        | User(id) ->
            let user =
                let idList = sprintf "%d" id
                query {
                    for u in context.User do
                    where (u.Type = UserType.Lookup && u.UserIdList = idList)
                    select u
                } |> Seq.head
            log.Info(sprintf "Getting codepoints for user %O: %O %O" user.ScreenNameResponse user.Name user.Description)
            Some { Mention = mention
                   TargetTweetEmbedHtml = null
                   TargetText = null
                   Codepoints = null
                   TargetUserScreenName = user.ScreenNameResponse
                   TargetUserDisplayName = user.Name
                   TargetUserSummary = user.Description
                   UserScreenNameCodepoints = Unicode.codepointInfo user.ScreenNameResponse
                   UserDisplayNameCodepoints = Unicode.codepointInfo user.Name
                   UserSummaryCodepoints = Unicode.codepointInfo user.Description }

    match reply with
    | None -> ()
    | Some(r) ->
        let dataRow = RequestDataRow(PartitionKey = (r.Mention.StatusID % 100uL).ToString(),
                                     RowKey = r.Mention.StatusID.ToString(),
                                     CodepointJson = JsonConvert.SerializeObject(r.Codepoints),
                                     TargetText = r.TargetText,
                                     TargetScreenNameCodepointJson = JsonConvert.SerializeObject(r.UserScreenNameCodepoints),
                                     TargetDisplayNameCodepointJson = JsonConvert.SerializeObject(r.UserDisplayNameCodepoints),
                                     TargetSummaryCodepointJson = JsonConvert.SerializeObject(r.UserSummaryCodepoints),
                                     TargetEmbedHtml = r.TargetTweetEmbedHtml,
                                     TargetScreenName = r.TargetUserScreenName,
                                     TargetDisplayName = r.TargetUserDisplayName,
                                     TargetSummary = r.TargetUserSummary,
                                     CreatedAt = DateTimeOffset(r.Mention.CreatedAt),
                                     MentionEmbedHtml = r.Mention.EmbedHtml,
                                     MentionStatusID = r.Mention.StatusID,
                                     MentionText = r.Mention.Text,
                                     MentionUrl = r.Mention.Url)
        requestDataTable.Add(dataRow)
        replyQueue.Add(mention)

    with
    | e -> log.Error(e.ToString())
