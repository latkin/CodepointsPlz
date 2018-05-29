namespace CodepointsPlz.Shared

open System
open Microsoft.Azure.WebJobs.Host

type LogDrain =
    { Info : string -> unit
      Warn : string -> unit
      Error : string -> unit }

module LogDrain =
    let console =
        { Info = Console.WriteLine
          Warn = Console.WriteLine
          Error = Console.WriteLine }

    let fromTraceWriter (writer : TraceWriter) =
        { Info = writer.Info
          Warn = writer.Warning
          Error = writer.Error }

module Log =
    let mutable private drain = LogDrain.console
    let init logDrain = drain <- logDrain

    let info fmt = Printf.ksprintf drain.Info fmt
    let warn fmt = Printf.ksprintf drain.Warn fmt
    let error fmt = Printf.ksprintf drain.Error fmt