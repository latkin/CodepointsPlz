module WebBackend

open System
open System.Linq
open System.Net
open System.Net.Http
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json
open CodepointsPlz.Shared
open CodepointsPlz.Shared.Storage

let Run(req: HttpRequestMessage,
        requestDataTable: IQueryable<RequestDataRow>,
        log: TraceWriter) =
    let id = ref 0uL
    let idParam =
        req.GetQueryNameValuePairs()
        |> Seq.tryFind (fun q -> q.Key = "tid" && UInt64.TryParse(q.Value, id))
    
    match idParam with
    | None ->
        req.CreateResponse(HttpStatusCode.BadRequest, "No valid tweet ID provided")
    | Some _ ->
        log.Info(sprintf "Loading page for tid %d" !id)
    
        match Storage.lookupReplyInfo requestDataTable !id with
        | Some(webData) ->
            let response = req.CreateResponse(HttpStatusCode.OK)
            response.Content <- new StringContent(JsonConvert.SerializeObject(webData))
            response
        | None ->
            req.CreateResponse(HttpStatusCode.BadRequest, sprintf "Tweet ID %O not found" !id)
