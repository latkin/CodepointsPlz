#if VS
module run
#else
#r "System.Net.Http"
#r "Newtonsoft.Json"
#r "Microsoft.WindowsAzure.Storage"
#r "System.Web"
#r "System.Linq.Expressions"
#r "System.Collections"
#r "PuppeteerSharp.dll"
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
open PuppeteerSharp

type Settings =
    { TwitterApiKey : string
      TwitterApiSecret : string
      TwitterAccessToken : string
      TwitterAccessTokenSecret : string
      BitlyAccessToken : string } with

    static member load () = 
        { TwitterApiKey =
            Environment.GetEnvironmentVariable("APPSETTING_twitterapikey", EnvironmentVariableTarget.Process)
          TwitterApiSecret =
            Environment.GetEnvironmentVariable("APPSETTING_twitterapisecret", EnvironmentVariableTarget.Process)
          TwitterAccessToken =
            Environment.GetEnvironmentVariable("APPSETTING_twitteraccesstoken", EnvironmentVariableTarget.Process)
          TwitterAccessTokenSecret =
            Environment.GetEnvironmentVariable("APPSETTING_twitteraccesstokensecret", EnvironmentVariableTarget.Process)
          BitlyAccessToken =
            Environment.GetEnvironmentVariable("APPSETTING_bitlyaccesstoken", EnvironmentVariableTarget.Process)
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

let shorten url settings (log: TraceWriter)=
    let bitlyUrl = 
        sprintf "https://api-ssl.bitly.com/v3/shorten?access_token=%s&longUrl=%s&format=txt" settings.BitlyAccessToken (Uri.EscapeDataString(url))

    let response =
        (new HttpClient()).GetAsync(bitlyUrl)
        |> Async.AwaitTask
        |> Async.RunSynchronously

    let content =
        response.Content.ReadAsStringAsync()
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    if response.IsSuccessStatusCode then
        content.Trim(' ', '\r', '\n')
    else
        log.Error(sprintf "Got error %O from Bitly - %s" response.StatusCode (content.Trim(' ', '\r', '\n')))
        url

let getImage url (log: TraceWriter) =
    let downloader = Downloader.CreateDefault()
    log.Info "Downloading..."

    downloader.DownloadRevisionAsync(Downloader.DefaultRevision)
    |> Async.AwaitTask
    |> Async.RunSynchronously

    async {
        log.Info "Browser"
        
        let! browser =
            Puppeteer.LaunchAsync(LaunchOptions(Headless = true, ExecutablePath = downloader.GetExecutablePath(Downloader.DefaultRevision)), Downloader.DefaultRevision)
            |> Async.AwaitTask

        log.Info "New page"
        let! page = browser.NewPageAsync() |> Async.AwaitTask

        log.Info "Go to page"
        do! page.GoToAsync(url) |> Async.AwaitTask |> Async.Ignore

        log.Info "Wait"
        let! elem = page.WaitForSelectorAsync("div.codepoint-table") |> Async.AwaitTask
      //  do! page.SetViewport(ViewPortOptions(.
        do! page.SetViewport(ViewPortOptions(Width = 2)) |> Async.AwaitTask
        
        let! x = page.EvaluateFunctionAsync("""selector => {
                    const element = document.querySelector(selector);
                    const {x, y, width, height} = element.getBoundingClientRect();
                    return "" + x + "|" + y + "|" + width + "|" + height;
                    }""", "div.codepoint-table")
                    |> Async.AwaitTask

        log.Info <| sprintf "Result: %O" x
        let [|x;y;w;h|] = (string x).Split('|') |> Array.map int

        log.Info "Screenshot"
        let tmpFile = Path.GetTempFileName() + ".png"
        do! page.ScreenshotAsync(
              tmpFile,
              ScreenshotOptions(Clip = Clip(X = x, Y= y, Height = h, Width = w))) |> Async.AwaitTask
        return tmpFile
    } |> Async.RunSynchronously

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

    let fullUrl = sprintf "https://latkin.github.io/CodepointsPlz/Website/?tid=%d" mention.StatusID
    let shortUrl = shorten fullUrl settings log

    let imgPath = getImage fullUrl
    log.Info(sprintf "Screenshot saved to %s" imgPath)

    let status = 
        context.ReplyAsync(mention.StatusID, sprintf "@%s ➡️ %s ⬅️" mention.ScreenName shortUrl)
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    log.Info(sprintf "Replied with status https://twitter.com/codepointsplz/status/%d" status.StatusID)
    ()