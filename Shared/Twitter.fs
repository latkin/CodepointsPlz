namespace CodepointsPlz.Shared

open System.IO
open LinqToTwitter

type Twitter(settings) =
    let context = lazy (
        let credentials = SingleUserInMemoryCredentialStore()
        credentials.ConsumerKey <- settings.TwitterApiKey
        credentials.ConsumerSecret <- settings.TwitterApiSecret
        credentials.AccessToken <- settings.TwitterAccessToken
        credentials.AccessTokenSecret <- settings.TwitterAccessTokenSecret
        let authorizer = SingleUserAuthorizer()
        authorizer.CredentialStore <- credentials
        new TwitterContext(authorizer)
    )
    
    member __.GetTweet(id : uint64) =
        let idList = sprintf "%d" id
        query {
            for tweet in context.Value.Status do
            where (tweet.Type = StatusType.Lookup && tweet.TweetIDs = idList && (int tweet.TweetMode) = 1)
            select tweet
        } |> Seq.head


    member __.GetUser(id : uint64) =
        let idList = sprintf "%d" id
        query {
            for u in context.Value.User do
            where (u.Type = UserType.Lookup && u.UserIdList = idList)
            select u
        } |> Seq.head

    member __.MentionsSince(latestMention) =
        match latestMention with
        | 0uL ->
            query {
                for tweet in context.Value.Status do
                where (tweet.Type = StatusType.Mentions && (int tweet.TweetMode) = 1)
                select tweet
            } :> seq<Status>
        | id ->
            query {
                for tweet in context.Value.Status do
                where (tweet.Type = StatusType.Mentions && tweet.SinceID = id  && (int tweet.TweetMode) = 1)
                where (tweet.StatusID <> id)
                select tweet
            } :> seq<Status>
    
    member __.GetEmbedHtml(status : Status) =
        query {
            for tweet in context.Value.Status do
            where (tweet.Type = StatusType.Oembed && tweet.ID = status.StatusID && (int tweet.TweetMode) = 1)
            select tweet.EmbeddedStatus
        } |> Seq.map (fun e -> e.Html) |> Seq.head

    member __.ReplyWithImage(replyToId : uint64, replyText, imageBytes) =
        async {
            let! media = 
                context.Value.UploadMediaAsync(imageBytes, "image/png", "tweet_image")
                |> Async.AwaitTask
    
            return!
                context.Value.ReplyAsync(replyToId, replyText, autoPopulateReplyMetadata = true, mediaIds = [|media.MediaID|])
                |> Async.AwaitTask
        }