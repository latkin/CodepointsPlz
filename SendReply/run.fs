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
open System.Diagnostics

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

let getImage toolsDir url (log: TraceWriter) =
    log.Info(sprintf "Contents of toolsDir: %A" (Directory.GetFiles(toolsDir)))

    let wkPath = Path.Combine(toolsDir, "wkhtmltoimage.exe")
    let tmpFile = Path.GetTempFileName() + ".png"
    let args = sprintf "--window-status print_ready --width 500 %s %s" url tmpFile

    log.Info(sprintf "Starting  %s %s" wkPath args)
    let psi = ProcessStartInfo(
                FileName = wkPath,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
                )
    let proc = Process.Start(psi)
        
    if proc.WaitForExit(30 * 1000) then
        log.Info("Process completed")
        log.Info(sprintf "Std out: %A" (proc.StandardOutput.ReadToEnd()))
        log.Info(sprintf "Std err: %A" (proc.StandardError.ReadToEnd()))

        proc.Refresh()
        if proc.ExitCode = 0 then Some(tmpFile)
        else None
    else
        log.Info("Process hung. Killing...")
        proc.Kill()
        log.Info(sprintf "Std out: %A" (proc.StandardOutput.ReadToEnd()))
        log.Info(sprintf "Std err: %A" (proc.StandardError.ReadToEnd()))
        None

let Run(mention: Mention,
        log: TraceWriter,
        functionContext : ExecutionContext) =
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

    let imagePath = getImage (Path.Combine(functionContext.FunctionDirectory, "wkhtmltoimage")) (sprintf "%s&to" fullUrl) log
    log.Info(sprintf "Screenshot path: %A" imagePath)

    let status = 
        context.ReplyAsync(mention.StatusID, sprintf "@%s ➡️ %s ⬅️" mention.ScreenName shortUrl)
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    log.Info(sprintf "Replied with status https://twitter.com/codepointsplz/status/%d" status.StatusID)
    ()