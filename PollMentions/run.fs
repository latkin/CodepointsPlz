module PollMentions

open System
open System.Web
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open CodepointsPlz.Shared
open CodepointsPlz.Shared.Storage

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

let Run(myTimer: TimerInfo, 
        mentionQueue: ICollector<Mention>,
        inLatestMentionRow : LatestMentionRow,
        log: TraceWriter) =
    async {
        log.Info(sprintf "Polling mentions after %O" inLatestMentionRow.LatestMention)

        let settings = Settings.load ()
        let twitter = Twitter(settings)
        let latestMention =
            inLatestMentionRow.LatestMention
            |> Option.ofObj
            |> Option.map uint64
            |> Option.defaultValue 0uL
            
        let newMentions = twitter.MentionsSince(latestMention)
    
        let (mentionCount, newLatestMention) =
            newMentions
            |> Seq.fold (fun (count, latest) newMention ->
                let users =
                    newMention.Entities.UserMentionEntities.ToArray()
                    |> Array.map (fun ume ->
                        { UserID = ume.Id
                          ScreenName = ume.ScreenName
                          Start = ume.Start
                          End = ume.End })
            
                let queuedMention = {
                    Text = if String.IsNullOrEmpty(newMention.FullText) then "<none>" else HttpUtility.HtmlDecode(newMention.FullText)
                    CreatedAt = newMention.CreatedAt
                    Url = sprintf "https://twitter.com/%O/status/%d" newMention.User.ScreenNameResponse newMention.StatusID
                    ScreenName = newMention.User.ScreenNameResponse
                    UserMentions = users
                    QuotedTweet = newMention.QuotedStatusID
                    InReplyToTweet = newMention.InReplyToStatusID
                    EmbedHtml = twitter.GetEmbedHtml(newMention)
                    StatusID = newMention.StatusID
                }

                mentionQueue.Add(queuedMention)
                (count + 1, max latest newMention.StatusID)) (0, latestMention)
    
        if newLatestMention > latestMention then
            do! newLatestMention |> Storage.saveLatestMention settings

        log.Info(sprintf "Enqueued %d mentions, new latest mention is %d" mentionCount newLatestMention)
    } |> Async.RunSynchronously