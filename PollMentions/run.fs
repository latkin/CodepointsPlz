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
open Microsoft.WindowsAzure.Storage.Table
open Newtonsoft.Json
open LinqToTwitter
open Microsoft.WindowsAzure.Storage

type LatestMentionRow() =
   inherit TableEntity()
   member val LatestMention : string = null with get, set

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

type Settings =
    { TwitterApiKey : string
      TwitterApiSecret : string
      TwitterAccessToken : string
      TwitterAccessTokenSecret : string
      StorageConnectionString : string 
      StorageTableName : string } with

    static member load () = 
        { TwitterApiKey =
            Environment.GetEnvironmentVariable("APPSETTING_twitterapikey", EnvironmentVariableTarget.Process)
          TwitterApiSecret =
            Environment.GetEnvironmentVariable("APPSETTING_twitterapisecret", EnvironmentVariableTarget.Process)
          TwitterAccessToken =
            Environment.GetEnvironmentVariable("APPSETTING_twitteraccesstoken", EnvironmentVariableTarget.Process)
          TwitterAccessTokenSecret =
            Environment.GetEnvironmentVariable("APPSETTING_twitteraccesstokensecret", EnvironmentVariableTarget.Process)
          StorageConnectionString =
            Environment.GetEnvironmentVariable("APPSETTING_codepointsplz_STORAGE", EnvironmentVariableTarget.Process)
          StorageTableName =
            Environment.GetEnvironmentVariable("APPSETTING_storagetablename", EnvironmentVariableTarget.Process)
        }

let saveLatestMention id settings =
    let table =
        CloudStorageAccount.Parse(settings.StorageConnectionString)
            .CreateCloudTableClient()
            .GetTableReference(settings.StorageTableName)

    let row = LatestMentionRow(PartitionKey = "latest-mention",
                               RowKey = "latest-mention",
                               LatestMention = id)
    let result =
        table.ExecuteAsync(TableOperation.InsertOrReplace(row))
        |> Async.AwaitTask
        |> Async.RunSynchronously

    result.Result :?> LatestMentionRow

 
let Run(myTimer: TimerInfo, 
        mentionQueue: ICollector<Mention>,
        inLatestMentionRow : LatestMentionRow,
        log: TraceWriter) =
    try
    log.Info(sprintf "Polling mentions after %O" inLatestMentionRow.LatestMention)

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
        match inLatestMentionRow.LatestMention with
        | null -> 0uL
        | s -> uint64 s

    let mentions =
        match effectiveLatestMention with
        | 0uL ->
            query {
                for tweet in context.Status do
                where (tweet.Type = StatusType.Mentions && (int tweet.TweetMode) = 1)
                select tweet
            }
        | id ->
            query {
                for tweet in context.Status do
                where (tweet.Type = StatusType.Mentions && tweet.SinceID = id  && (int tweet.TweetMode) = 1)
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
                      ScreenName = ume.ScreenName
                      Start = ume.Start
                      End = ume.End }
                )
            
            log.Info("Getting embed info")
            let embedInfo =
                query {
                    for tweet in context.Status do
                    where (tweet.Type = StatusType.Oembed && tweet.ID = m.StatusID && (int tweet.TweetMode) = 1)
                    select tweet.EmbeddedStatus
                } |> Seq.head

            let mention = {
                Text = if String.IsNullOrEmpty(m.Text) then "<none>" else m.Text
                CreatedAt = m.CreatedAt
                Url = sprintf "https://twitter.com/%O/status/%d" m.User.ScreenNameResponse m.StatusID
                ScreenName = m.User.ScreenNameResponse
                UserMentions = users
                QuotedTweet = m.QuotedStatusID
                InReplyToTweet = m.InReplyToStatusID
                EmbedHtml = embedInfo.Html
                StatusID = m.StatusID
            }
            mentionQueue.Add(mention)
            (count + 1, Math.Max(latest, m.StatusID))) (0, effectiveLatestMention)
    
    if newLatestMention > effectiveLatestMention then
        log.Info("Inserting new latest into table")
        let newLatest = saveLatestMention (newLatestMention.ToString()) settings
        log.Info(sprintf "Successfully wrote %O as new latest" newLatest.LatestMention)

    log.Info(sprintf "Enqueued %d mentions, new latest mention is %d" mentionCount newLatestMention)
    with
    | e -> log.Error(e.ToString())