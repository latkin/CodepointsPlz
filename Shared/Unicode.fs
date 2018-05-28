namespace CodepointsPlz.Shared

open System
open System.IO
open System.Collections.Generic
open System.Text.RegularExpressions

[<CLIMutable>]
type Codepoint =
    { Codepoint : int
      Name : string }

type private Store =
    { codepoints : Dictionary<int, string>
      ranges : List<(int * int)> }

type UnicodeLookup(dataFilePath : string) =
    let parsePatt = """^\<(?<rangeName>[a-zA-Z0-9 ]+?), (?<marker>First|Last)>$"""

    // UnicodeData.txt fields documented at http://www.unicode.org/Public/5.1.0/ucd/UCD.html#UnicodeData.txt
    // typical line:
    // 0033;DIGIT THREE;Nd;0;EN;;3;3;3;N;;;;;
    // <control> line:
    // 000D;<control>;Cc;0;B;;;;;N;CARRIAGE RETURN (CR);;;;
    // range lines:
    // 17000;<Tangut Ideograph, First>;Lo;0;L;;;;;N;;;;;
    // 187F1;<Tangut Ideograph, Last>;Lo;0;L;;;;;N;;;;;
    let (|Single|RangeStart|RangeEnd|Unknown|) line =
        if String.IsNullOrEmpty(line) then Unknown else
        let fields = line.Split(';')
        let codepoint = Convert.ToInt32(fields.[0], 16)
        match Regex.Match(fields.[1], parsePatt) with
        | m when m.Success ->
            match m.Groups.["marker"].Value with
            | "First" -> RangeStart(codepoint)
            | "Last" -> RangeEnd(codepoint, m.Groups.["rangeName"].Value)
            | _ -> Unknown
        | _ ->
            let name = fields.[1]
            let altName = fields.[10]
            let finalName =
                if name.StartsWith("<") && name.EndsWith(">") && not (String.IsNullOrEmpty(altName)) then
                    sprintf "%O %O" name altName
                else
                    name
            Single(codepoint, finalName)

    let store = lazy (
        let store = { codepoints = Dictionary<int, string>(); ranges = List() }
        File.ReadLines(dataFilePath)
        |> Seq.fold (fun rangeStart -> function
            | Unknown -> rangeStart
            | RangeStart(codepoint) -> codepoint
            | RangeEnd(codepoint, rangeName) ->
                store.codepoints.Add(rangeStart, rangeName)
                store.ranges.Add((rangeStart, codepoint))
                rangeStart
            | Single(codepoint, name) ->
                store.codepoints.Add(codepoint, name)
                rangeStart
        ) 0 |> ignore
        store
    )

    let lookupName c =
        if c < 0 || c > 0x10ffff then "INVALID CODEPOINT" else
        match store.Value.codepoints.TryGetValue(c) with
        | true, n -> n
        | _ ->
            // there are < 20 ranges so linear search isn't bad
            let (rangeStart, _) = store.Value.ranges |> Seq.find (fun (a, b) -> c >= a && c <= b)
            let n = store.Value.codepoints.[rangeStart]
            store.Value.codepoints.[c] <- n
            n

    member __.GetCodepoints(s : string) =
        let cps = List<Codepoint>()
        let mutable i = 0
        while i < s.Length do
            let codepoint =
                // BMP or paired surrogate
                try Char.ConvertToUtf32(s, i) with
                // unpaired surrogate
                | _ -> int s.[i]

            cps.Add({ Codepoint = codepoint
                      Name = lookupName codepoint })

            let isPairedHS =
                Char.IsHighSurrogate(s.[i]) && i < (s.Length - 1) && Char.IsLowSurrogate(s.[i+1])

            if isPairedHS then
                i <- i + 2
            else
                i <- i + 1

        cps.ToArray()
