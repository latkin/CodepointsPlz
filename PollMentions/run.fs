module PollMentions

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open CodepointsPlz.Shared
open CodepointsPlz.Shared.Storage

let Run(_myTimer: TimerInfo, 
        latestMentionRow : LatestMentionRow,
        mentionQueue: ICollector<TriggerMention>,
        log: TraceWriter) =
    
    async {
        log |> LogDrain.fromTraceWriter |> Log.init
        Log.info "PollMentions: %O" latestMentionRow.LatestMention

        let settings = Settings.load ()
        let twitter = Twitter(settings)

        let latestMention =
            latestMentionRow.LatestMention
            |> Option.ofObj
            |> Option.map uint64
            |> Option.defaultValue 0uL
        
        let newMentionCount, newLatestMention =
            twitter.MentionsSince(latestMention)
            |> Seq.map (fun mention -> mention.StatusID)
            |> Seq.fold (fun (mentionCount, newLatestMention) id ->
                mentionQueue.Add({ StatusID = id
                                   DirectTrigger = false })
                (mentionCount + 1, max newLatestMention id)) (0, latestMention)
    
        if newLatestMention > latestMention then
            do! newLatestMention |> Storage.saveLatestMention settings

        Log.info "Enqueued %d mentions, new latest mention is %d" newMentionCount newLatestMention
        twitter.WaitForRateLimiter()
    } |> Async.RunSynchronously