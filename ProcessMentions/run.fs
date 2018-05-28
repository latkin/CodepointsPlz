module ProcessMentions

open System
open System.IO
open System.Text.RegularExpressions
open System.Web
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.WindowsAzure.Storage.Table
open LinqToTwitter
open Newtonsoft.Json
open CodepointsPlz.Shared

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
type Reply =
    { Mention : Mention

      TargetTweetEmbedHtml : string

      TargetText : string
      Codepoints : Codepoint[]

      TargetUserScreenName : string
      TargetUserDisplayName : string
      TargetUserSummary : string
      UserScreenNameCodepoints : Codepoint[]
      UserDisplayNameCodepoints : Codepoint[]
      UserSummaryCodepoints : Codepoint[] }

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

let Run(mention: Mention,
        replyQueue: ICollector<Mention>,
        requestDataTable: ICollector<RequestDataRow>,
        log: TraceWriter,
        functionContext : ExecutionContext) =
    try
    log.Info(sprintf "Processing mention %O" mention.Url)
    log.Info(sprintf "Text: %O" mention.Text)

    let unicode = UnicodeLookup(Path.Combine(functionContext.FunctionDirectory, "UnicodeData.txt"))

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
            let codepoints = unicode.GetCodepoints(s)
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
            let codepoints = unicode.GetCodepoints(decodedText)
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
                   UserScreenNameCodepoints = unicode.GetCodepoints(user.ScreenNameResponse)
                   UserDisplayNameCodepoints = unicode.GetCodepoints(user.Name)
                   UserSummaryCodepoints = unicode.GetCodepoints(user.Description) }

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
