namespace CodepointsPlz.Shared

open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Table

module Storage =
    type LatestMentionRow() =
       inherit TableEntity()
       member val LatestMention : string = null with get, set

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