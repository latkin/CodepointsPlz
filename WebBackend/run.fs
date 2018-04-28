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
open System.Net
open System.Net.Http
open System.Text.RegularExpressions
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json
open Microsoft.WindowsAzure.Storage.Table
open System.Linq

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

[<CLIMutable>]
 type CodepointInfo =
    { Codepoint : int
      Name : string }

type WebData = 
    { Codepoints : CodepointInfo array
      MentionEmbedHtml : string
      Text : string
      TargetEmbedHtml : string
      ScreenName : string
      DisplayName : string
      Summary : string
      ScreenNameCodepoints : CodepointInfo array
      DisplayNameCodepoints : CodepointInfo array
      SummaryCodepoints : CodepointInfo array }

let Run(req: HttpRequestMessage,
        requestDataTable: IQueryable<RequestDataRow>,
        log: TraceWriter) =
    async {
        let idParam =
            req.GetQueryNameValuePairs()
            |> Seq.tryFind (fun q -> q.Key = "tid" && Regex.IsMatch(q.Value, "^\d{2,100}"))

        match idParam with
        | Some idParam ->
            let id = idParam.Value
            log.Info(sprintf "Loading page for tid %O" id)

            let partitionKey = id.Substring(id.Length - 2, 2)
            let entries = 
                query {
                    for row in requestDataTable do
                    where (row.PartitionKey = partitionKey && row.RowKey = id)
                    select row
                } |> Array.ofSeq
            
            match entries with
            | [| entry |] ->
                let body = { Codepoints = JsonConvert.DeserializeObject<CodepointInfo[]>(entry.CodepointJson)
                             MentionEmbedHtml = entry.MentionEmbedHtml
                             Text = entry.TargetText
                             TargetEmbedHtml = entry.TargetEmbedHtml
                             ScreenName = entry.TargetScreenName
                             DisplayName = entry.TargetDisplayName
                             Summary = entry.TargetSummary
                             ScreenNameCodepoints = JsonConvert.DeserializeObject<CodepointInfo[]>(entry.TargetScreenNameCodepointJson)
                             DisplayNameCodepoints = JsonConvert.DeserializeObject<CodepointInfo[]>(entry.TargetDisplayNameCodepointJson)
                             SummaryCodepoints = JsonConvert.DeserializeObject<CodepointInfo[]>(entry.TargetSummaryCodepointJson) }
                let response = req.CreateResponse(HttpStatusCode.OK)
                response.Content <- new StringContent(JsonConvert.SerializeObject(body))
                return response
            | _ ->
                return req.CreateResponse(HttpStatusCode.BadRequest, sprintf "Tweet ID %O not found" id);
        | None ->
            return req.CreateResponse(HttpStatusCode.BadRequest, "No valid tweet ID provided");
    } |> Async.RunSynchronously