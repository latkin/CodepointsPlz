module SendReply

open System
open System.Net.Http
open System.Security.Cryptography
open System.Text
open Microsoft.Azure.WebJobs.Host
open CodepointsPlz.Shared

let shorten url settings =
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
            Log.error "Got error %O from Bitly - %s" response.StatusCode (content.Trim(' ', '\r', '\n'))
            return url
    }

let getImage targetUrl settings =
    async {
        let urlParams = sprintf "url=%s&vw=500&vh=10&waitFor=.codepoint-table&full=true" (Uri.EscapeDataString(targetUrl))
        let hashTarget = sprintf "%s%s" settings.ScreenshotApiSecret urlParams
        let hash = BitConverter.ToString(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(hashTarget))).Replace("-", "").ToLower()
        let screenshotUrl =
            sprintf "https://cdn.capture.techulus.in/%s/%s/Image?%s" settings.ScreenshotApiKey hash urlParams
    
        Log.info "Getting screenshot from %s" screenshotUrl

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
        log |> LogDrain.fromTraceWriter |> Log.init
        Log.info "Replying to mention: %A" mention.Url

        let settings = Settings.load ()
        let twitter = Twitter(settings)

        let fullUrl = sprintf "https://latkin.github.io/CodepointsPlz/Website/?tid=%d" mention.StatusID
        let! shortUrl = shorten fullUrl settings
        
        let! imageBytes = getImage (sprintf "%s&to" fullUrl) settings
        Log.info "Got screenshot, uploading to Twitter"
        
        let replyText = (sprintf "➡️ %s ⬅️" shortUrl)
        let! reply = twitter.ReplyWithImage(mention.StatusID, replyText, imageBytes)

        Log.info "Replied with status https://twitter.com/codepointsplz/status/%d" reply.StatusID
    } |> Async.RunSynchronously
