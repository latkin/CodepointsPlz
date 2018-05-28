module SendReply

open System
open System.Net.Http
open System.Security.Cryptography
open System.Text
open Microsoft.Azure.WebJobs.Host
open CodepointsPlz.Shared

let shorten url settings (log: TraceWriter) =
    async {
        let bitlyUrl = 
            sprintf "https://api-ssl.bitly.com/v3/shorten?access_token=%s&longUrl=%s&format=txt" settings.BitlyAccessToken (Uri.EscapeDataString(url))

        let! response =
            (new HttpClient()).GetAsync(bitlyUrl)
            |> Async.AwaitTask

        let! content =
            response.Content.ReadAsStringAsync()
            |> Async.AwaitTask
    
        if response.IsSuccessStatusCode then
            return content.Trim(' ', '\r', '\n')
        else
            log.Error(sprintf "Got error %O from Bitly - %s" response.StatusCode (content.Trim(' ', '\r', '\n')))
            return url
    }

let getImage targetUrl settings (log: TraceWriter) =
    async {
        let urlParams = sprintf "url=%s&vw=500&vh=10&waitFor=.codepoint-table&full=true" (Uri.EscapeDataString(targetUrl))
        let hashTarget = sprintf "%s%s" settings.ScreenshotApiSecret urlParams
        let hash = BitConverter.ToString(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(hashTarget))).Replace("-", "").ToLower()
        let screenshotUrl =
            sprintf "https://cdn.capture.techulus.in/%s/%s/Image?%s" settings.ScreenshotApiKey hash urlParams
    
        log.Info(sprintf "Getting screenshot from %s" screenshotUrl)

        let! response =
            (new HttpClient()).GetAsync(screenshotUrl)
            |> Async.AwaitTask

        return!
            response.Content.ReadAsByteArrayAsync()
            |> Async.AwaitTask
    }

let Run(mention: Mention,
        log: TraceWriter) =
    async {
        log.Info(sprintf "Replying to mention %O" mention.Url)

        let settings = Settings.load ()
        let twitter = Twitter(settings)

        let fullUrl = sprintf "https://latkin.github.io/CodepointsPlz/Website/?tid=%d" mention.StatusID
        let! shortUrl = shorten fullUrl settings log
        
        let! imageBytes = getImage (sprintf "%s&to" fullUrl) settings log
        log.Info("Got screenshot, uploading to Twitter")
        
        let replyText = (sprintf "➡️ %s ⬅️" shortUrl)
        let! reply = twitter.ReplyWithImage(mention.StatusID, replyText, imageBytes)

        log.Info(sprintf "Replied with status https://twitter.com/codepointsplz/status/%d" reply.StatusID)
    } |> Async.RunSynchronously