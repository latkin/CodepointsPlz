module ProcessMentions

open System.IO
open System.Text.RegularExpressions
open System.Web
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open CodepointsPlz.Shared
open CodepointsPlz.Shared.Storage

module CodepointRequest =
    let private trim (s:string) = s.Trim(' ','\r', '\n', '\u00A0')
    let private hasUpArrow s = Regex.IsMatch(s, "\\u2B06")
    let private hasDownArrow s = Regex.IsMatch(s, "\\u2B07")
    let private hasRightArrow s = Regex.IsMatch(s, "\\u27A1")
    let private afterRightArrow s =
        match Regex.Match(s, "\\u27A1(?:\\uFE0F)?(.*)", RegexOptions.Singleline) with
        | m when not m.Success -> None
        | m -> m.Groups.[1].Value |> trim |> Some
 
    let getFullMention (twitter : Twitter) id =
        let tweet = twitter.GetTweet(id)
        let embedHtml = twitter.GetEmbedHtml(tweet)
        let users =
            tweet.Entities.UserMentionEntities.ToArray()
            |> Array.map (fun ume ->
                { UserID = ume.Id
                  ScreenName = ume.ScreenName })
    
        { Text = HttpUtility.HtmlDecode(tweet.FullText)
          CreatedAt = tweet.CreatedAt
          Url = sprintf "https://twitter.com/%O/status/%d" tweet.User.ScreenNameResponse tweet.StatusID
          ScreenName = tweet.User.ScreenNameResponse
          UserMentions = users
          QuotedTweet = tweet.QuotedStatusID
          InReplyToTweet = tweet.InReplyToStatusID
          EmbedHtml = embedHtml
          StatusID = tweet.StatusID }

    let analyze mention =
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

let generateReply (twitter : Twitter) (unicode : UnicodeLookup) (triggerMention : TriggerMention) (fullMention : Mention) =
    if triggerMention.DirectTrigger then
        Log.info "Request is a direct trigger"
        let codepoints = unicode.GetCodepoints(fullMention.Text)

        Some { Mention = fullMention
               TargetTweetEmbedHtml = null
               TargetText = fullMention.Text
               Codepoints = codepoints
               TargetUserScreenName = null
               TargetUserDisplayName = null
               TargetUserSummary = null
               UserScreenNameCodepoints = null 
               UserDisplayNameCodepoints = null
               UserSummaryCodepoints = null }
    else
        let cpRequest = CodepointRequest.analyze fullMention
        Log.info "Request parsed as %A" cpRequest

        match cpRequest with
        | Blank -> None
        | PlainText(s) ->
            let codepoints = unicode.GetCodepoints(s)

            Some { Mention = fullMention
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

            Some { Mention = fullMention
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

            Some { Mention = fullMention
                   TargetTweetEmbedHtml = null
                   TargetText = null
                   Codepoints = null
                   TargetUserScreenName = user.ScreenNameResponse
                   TargetUserDisplayName = user.Name
                   TargetUserSummary = user.Description
                   UserScreenNameCodepoints = screenNameCodepoints
                   UserDisplayNameCodepoints = displayNameCodepoints
                   UserSummaryCodepoints = summaryCodepoints }

let Run(triggerMention: TriggerMention,
        replyQueue: ICollector<Mention>,
        requestDataTable: ICollector<RequestDataRow>,
        log: TraceWriter,
        functionContext : ExecutionContext) =

    log |> LogDrain.fromTraceWriter |> Log.init
    Log.info "ProcessMentions: %A" triggerMention

    let settings = Settings.load ()
    let twitter = Twitter(settings)
    let unicode = UnicodeLookup(Path.Combine(functionContext.FunctionDirectory, "UnicodeData.txt"))
    let fullMention = CodepointRequest.getFullMention twitter triggerMention.StatusID

    generateReply twitter unicode triggerMention fullMention
    |> Option.iter (fun reply ->
        requestDataTable.Add(RequestDataRow.fromReply reply)
        replyQueue.Add(fullMention)
    )
