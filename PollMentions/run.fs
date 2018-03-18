#if VS
module run
#else
#r "System.Net.Http"
#r "Newtonsoft.Json"
#r "Microsoft.WindowsAzure.Storage"
#r "System.Web"
#r "System.Linq.Expressions"
#r "LinqToTwitter"
#r "System.Collections"
#endif

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.WebJobs
open Newtonsoft.Json
open LinqToTwitter

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

let Run(myTimer: TimerInfo, 
        mentionQueue: ICollector<Mention>,
        inLatestMention : string,
        outLatestMention : byref<string>,
        log: TraceWriter) =
    try
    log.Info(sprintf "Polling mentions after %O" inLatestMention)

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

    let effectiveLatestMention =
        match inLatestMention with
        | null -> 0uL
        | s -> uint64 s

    let mentions =
        match effectiveLatestMention with
        | 0uL ->
            query {
                for tweet in context.Status do
                where (tweet.Type = StatusType.Mentions)
                select tweet
            }
        | id ->
            query {
                for tweet in context.Status do
                where (tweet.Type = StatusType.Mentions && tweet.SinceID = id)
                where (tweet.StatusID <> id)
                select tweet
            }
    
    let (mentionCount, newLatestMention) =
        mentions
        |> Seq.fold (fun (count, latest) m ->
            let users =
                m.Entities.UserMentionEntities.ToArray()
                |> Array.map (fun ume ->
                    { UserID = ume.Id
                      Start = ume.Start
                      End = ume.End }
                )
            let mention = {
                Text = if String.IsNullOrEmpty(m.Text) then "<none>" else m.Text
                CreatedAt = m.CreatedAt
                Url = sprintf "https://twitter.com/%O/status/%d" m.User.ScreenNameResponse m.StatusID
                UserMentions = users
                QuotedTweet = m.QuotedStatusID
                StatusID = m.StatusID
            }
            mentionQueue.Add(mention)
            (count + 1, Math.Max(latest, m.StatusID))) (0, effectiveLatestMention)
    
    if newLatestMention <> 0uL then
        outLatestMention <- newLatestMention.ToString()

    log.Info(sprintf "Enqueued %d mentions, new latest mention is %d" mentionCount newLatestMention)
    with
    | e -> log.Error(e.ToString())