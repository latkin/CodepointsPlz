module ProcessMentions

open System
open System.IO
open System.Text.RegularExpressions
open System.Web
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open CodepointsPlz.Shared
open CodepointsPlz.Shared.Storage

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

let Run(mention: Mention,
        replyQueue: ICollector<Mention>,
        requestDataTable: ICollector<RequestDataRow>,
        log: TraceWriter,
        functionContext : ExecutionContext) =

    log.Info(sprintf "Processing mention %O" mention.Url)
    log.Info(sprintf "Text: %O" mention.Text)

    let unicode = UnicodeLookup(Path.Combine(functionContext.FunctionDirectory, "UnicodeData.txt"))

    let cpRequest = CodepointRequest.analyze mention
    log.Info(sprintf "Request parsed as %A" cpRequest)

    let settings = Settings.load ()
    let twitter = Twitter(settings)

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
            let tweet = twitter.GetTweet(id)
            let embedHtml = twitter.GetEmbedHtml(tweet)
            let decodedText = HttpUtility.HtmlDecode(tweet.FullText)
            let codepoints = unicode.GetCodepoints(decodedText)

            Some { Mention = mention
                   TargetTweetEmbedHtml = embedHtml
                   TargetText = decodedText
                   Codepoints = codepoints
                   TargetUserScreenName = null
                   TargetUserDisplayName = null
                   TargetUserSummary = null
                   UserScreenNameCodepoints = null 
                   UserDisplayNameCodepoints = null
                   UserSummaryCodepoints = null }
        | User(id) ->
            let user = twitter.GetUser(id)
            let screenNameCodepoints = unicode.GetCodepoints(user.ScreenNameResponse)
            let displayNameCodepoints = unicode.GetCodepoints(user.Name)
            let summaryCodepoints = unicode.GetCodepoints(user.Description)

            Some { Mention = mention
                   TargetTweetEmbedHtml = null
                   TargetText = null
                   Codepoints = null
                   TargetUserScreenName = user.ScreenNameResponse
                   TargetUserDisplayName = user.Name
                   TargetUserSummary = user.Description
                   UserScreenNameCodepoints = screenNameCodepoints
                   UserDisplayNameCodepoints = displayNameCodepoints
                   UserSummaryCodepoints = summaryCodepoints }

    match reply with
    | None -> ()
    | Some(r) ->
        requestDataTable.Add(RequestDataRow.fromReply r)
        replyQueue.Add(mention)
