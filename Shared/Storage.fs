namespace CodepointsPlz.Shared

open System
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Table
open Newtonsoft.Json

module Storage =

    type LatestMentionRow() =
       inherit TableEntity()
       member val LatestMention : string = null with get, set

    type RequestDataRow() =
       inherit TableEntity()
       member val CodepointJson = null with get,set
       member val TargetScreenNameCodepointJson = null with get,set
       member val TargetDisplayNameCodepointJson = null with get,set
       member val TargetSummaryCodepointJson = null with get,set
       member val MentionText = null with get,set
       member val TargetText = null with get,set
       member val MentionUrl = null with get,set
       member val MentionStatusID = 0uL with get,set
       member val CreatedAt = DateTimeOffset.UtcNow with get,set
       member val MentionEmbedHtml = null with get,set
       member val TargetEmbedHtml = null with get,set
       member val TargetScreenName = null with get,set
       member val TargetDisplayName = null with get,set
       member val TargetSummary = null with get,set

       static member fromReply r =
            RequestDataRow(PartitionKey = (r.Mention.StatusID % 100uL).ToString(),
                           RowKey = r.Mention.StatusID.ToString(),
                           CodepointJson = JsonConvert.SerializeObject(r.Codepoints),
                           TargetText = r.TargetText,
                           TargetScreenNameCodepointJson = JsonConvert.SerializeObject(r.UserScreenNameCodepoints),
                           TargetDisplayNameCodepointJson = JsonConvert.SerializeObject(r.UserDisplayNameCodepoints),
                           TargetSummaryCodepointJson = JsonConvert.SerializeObject(r.UserSummaryCodepoints),
                           TargetEmbedHtml = r.TargetTweetEmbedHtml,
                           TargetScreenName = r.TargetUserScreenName,
                           TargetDisplayName = r.TargetUserDisplayName,
                           TargetSummary = r.TargetUserSummary,
                           CreatedAt = DateTimeOffset(r.Mention.CreatedAt),
                           MentionEmbedHtml = r.Mention.EmbedHtml,
                           MentionStatusID = r.Mention.StatusID,
                           MentionText = r.Mention.Text,
                           MentionUrl = r.Mention.Url)

    let saveLatestMention settings (id : uint64) =
        let table =
            CloudStorageAccount.Parse(settings.StorageConnectionString)
                .CreateCloudTableClient()
                .GetTableReference(settings.StorageTableName)

        let row = LatestMentionRow(PartitionKey = "latest-mention",
                                   RowKey = "latest-mention",
                                   LatestMention = string id)
        async {
            do!
                table.ExecuteAsync(TableOperation.InsertOrReplace(row))
                |> Async.AwaitTask
                |> Async.Ignore
        }
