module SendReply

open System
open System.Net.Http
open System.Security.Cryptography
open System.Text
open Microsoft.Azure.WebJobs.Host
open LinqToTwitter
open CodepointsPlz.Shared

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

let getImage targetUrl settings (log: TraceWriter) =

    let urlParams = sprintf "url=%s&vw=500&vh=10&waitFor=.codepoint-table&full=true" (Uri.EscapeDataString(targetUrl))
    let hashTarget = sprintf "%s%s" settings.ScreenshotApiSecret urlParams
    let hash = BitConverter.ToString(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(hashTarget))).Replace("-", "").ToLower()
    let screenshotUrl =
        sprintf "https://cdn.capture.techulus.in/%s/%s/Image?%s" settings.ScreenshotApiKey hash urlParams
    
    log.Info(sprintf "Getting screenshot from %s" screenshotUrl)
    async {
        let! response = (new HttpClient()).GetAsync(screenshotUrl) |> Async.AwaitTask
        return! response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
    } |> Async.RunSynchronously

let Run(mention: Mention,
        log: TraceWriter) =
    log.Info(sprintf "Replying to mention %O" mention.Url)

    let settings = Settings.load ()
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
    let imageBytes = getImage (sprintf "%s&to" fullUrl) settings log

    log.Info("Got screenshot, uploading to Twitter")
    let media = 
            context.UploadMediaAsync(imageBytes, "image/png", "tweet_image")
            |> Async.AwaitTask
            |> Async.RunSynchronously
    
    let status = 
        context.ReplyAsync(mention.StatusID, (sprintf "➡️ %s ⬅️" shortUrl), autoPopulateReplyMetadata = true, mediaIds = [|media.MediaID|])
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    log.Info(sprintf "Replied with status https://twitter.com/codepointsplz/status/%d" status.StatusID)
    ()