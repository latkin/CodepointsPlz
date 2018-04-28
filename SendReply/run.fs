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
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Table

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

let Run(mention: Mention,
        log: TraceWriter) =
    log.Info(sprintf "Replying to mention %O" mention.Url)

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

    let status = 
        context.ReplyAsync(mention.StatusID, sprintf "@%s https://latkin.github.io/CodepointsPlz/Website/?tid=%d" mention.ScreenName mention.StatusID)
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    log.Info(sprintf "Replied with status https://twitter.com/codepointsplz/status/%d" status.StatusID)
    ()