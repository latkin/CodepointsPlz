namespace CodepointsPlz.Shared
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