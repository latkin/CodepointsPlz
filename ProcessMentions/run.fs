#if VS
module run
#else
#r "System.Net.Http"
#r "Newtonsoft.Json"
#r "Microsoft.WindowsAzure.Storage"
#r "System.Web"
#r "System.Linq.Expressions"
#r "System.Collections"
#endif

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.WebJobs
open Newtonsoft.Json
open System.Text.RegularExpressions
open LinqToTwitter
open System.IO

type Settings =
    { TwitterApiKey : string
      TwitterApiSecret : string
      TwitterAccessToken : string
      TwitterAccessTokenSecret : string} with

    static member load () = 
        { TwitterApiKey =
            Environment.GetEnvironmentVariable("APPSETTING_twitterapikey", EnvironmentVariableTarget.Process)
          TwitterApiSecret =
            Environment.GetEnvironmentVariable("APPSETTING_twitterapisecret", EnvironmentVariableTarget.Process)
          TwitterAccessToken =
            Environment.GetEnvironmentVariable("APPSETTING_twitteraccesstoken", EnvironmentVariableTarget.Process)
          TwitterAccessTokenSecret =
            Environment.GetEnvironmentVariable("APPSETTING_twitteraccesstokensecret", EnvironmentVariableTarget.Process)
        }

[<CLIMutable>]
type UserMention =
    { UserID : uint64
      Start : int
      End : int }

[<CLIMutable>]
type Mention =
    { Text : string
      UserMentions : UserMention array
      QuotedTweet : uint64
      Url : string
      CreatedAt : DateTime
      StatusID : uint64 }

type CodepointRequest =
    | PlainText of text : string
    | Tweet of id : uint64
    | User of id : uint64
    | Blank

module CodepointRequest =
    let private afterMentionPattern = """@codepointsplz[ \r\n]*(.+?)$"""

    let trim (s:String) = s.Trim(' ','\r', '\n')

    let analyze (m : Mention) =
        if m.QuotedTweet <> 0uL then
            Tweet(m.QuotedTweet)
        else
            match m.UserMentions |> Array.tryLast with
            // no mentions -- should not be possible since we only run
            // this against tweets where we are mentioned
            | None -> Blank

            // we are the last mention -- treat everything after our mention as plain text
            | Some(um) when um.UserID = 971963654047330308uL ->
                match Regex.Match(m.Text, afterMentionPattern, RegexOptions.Singleline ||| RegexOptions.IgnoreCase) with
                | m when not m.Success -> Blank
                | m ->
                    let afterMention = m.Groups.[1].Value |> trim
                    PlainText(afterMention)
        
            // somebody else is the last mention -- consider this a request to analyze their profile
            | Some(um) -> User(um.UserID)

[<CLIMutable>]
 type CodepointInfo =
    { Codepoint : int
      Name : string }

[<CLIMutable>]
type Reply =
    { Codepoints : CodepointInfo array
      Mention : Mention }

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
        replyQueue: ICollector<Reply>,
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

    match cpRequest with
    | Blank -> ()
    | PlainText(s) ->
        let codepoints = Unicode.codepointInfo s
        replyQueue.Add({ Mention = mention; Codepoints = codepoints })
    | Tweet(id) ->
        let tweet =
            let idList = sprintf "%d" id
            query {
                for tweet in context.Status do
                where (tweet.Type = StatusType.Lookup && tweet.TweetIDs = idList)
                select tweet
            } |> Seq.head

        log.Info(sprintf "Getting codepoints for tweet %d: %O" tweet.StatusID tweet.Text)
        let codepoints = Unicode.codepointInfo tweet.Text
        replyQueue.Add({ Mention = mention; Codepoints = codepoints })

    | User(id) ->
        let user =
            let idList = sprintf "%d" id
            query {
                for u in context.User do
                where (u.Type = UserType.Lookup && u.UserIdList = idList)
                select u
            } |> Seq.head
        log.Info(sprintf "Getting codepoints for user %O: %O %O" user.ScreenNameResponse user.Name user.Description)
        let codepoints = Unicode.codepointInfo (sprintf "%O %O %O" user.ScreenNameResponse user.Name user.Description)
        replyQueue.Add({ Mention = mention; Codepoints = codepoints })

    with
    | e -> log.Error(e.ToString())


