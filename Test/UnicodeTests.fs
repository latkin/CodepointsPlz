namespace CodepointsPlz.Test

open System
open System.IO
open CodepointsPlz.Shared
open NUnit.Framework

type UnicodeTests () =
    let dataFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../UnicodeData.txt")

    let check s expected =
        let u = UnicodeLookup dataFilePath
        let cps = u.GetCodepoints s
        Assert.AreEqual(expected, cps)

    [<Test>]
    member __.LazyLoadThrowsOnWork () =
        let u = UnicodeLookup("/no/such/path")
        Assert.Throws<DirectoryNotFoundException> (fun () -> u.GetCodepoints("x") |> ignore) |> ignore

    [<Test>]
    member __.LazyLoadNoOp () =
        let u = UnicodeLookup("/no/such/path")
        let cps = u.GetCodepoints("")
        Assert.AreEqual([| |], cps)

    [<Test>]
    member __.SingleCodepoints () =
        check "abc" [|
            { Codepoint = 0x61; Name = "LATIN SMALL LETTER A" }
            { Codepoint = 0x62; Name = "LATIN SMALL LETTER B" }
            { Codepoint = 0x63; Name = "LATIN SMALL LETTER C" }
        |]

    [<Test>]
    member __.ControlChars () =
        check "\u0007\u008A" [|
            { Codepoint = 0x7; Name = "<control> BELL" }
            { Codepoint = 0x8A; Name = "<control> LINE TABULATION SET" }
        |]

    [<Test>]
    member __.Ranges () =
        // first cp in range, last cp in range, middle cp in range
        check "\u3400\u9FEF\uAD00" [|
            { Codepoint = 0x3400; Name = "CJK Ideograph Extension A" }
            { Codepoint = 0x9FEF; Name = "CJK Ideograph" }
            { Codepoint = 0xAD00; Name = "Hangul Syllable" }
        |]

    [<Test>]
    member __.ValidSurrogateOnly () =
        check "\uD83E\uDD2F" [|
            { Codepoint = 0x1F92F; Name = "SHOCKED FACE WITH EXPLODING HEAD" }
        |]

    [<Test>]
    member __.ValidSurrogateBeginning () =
        check "\uD83E\uDD2Fa" [|
            { Codepoint = 0x1F92F; Name = "SHOCKED FACE WITH EXPLODING HEAD" }
            { Codepoint = 0x61; Name = "LATIN SMALL LETTER A" }
        |]

    [<Test>]
    member __.UnpairedLowSurrogateOnly () =
        check (String([|char 0xDD2F|])) [|
            { Codepoint = 0xDD2F; Name = "Low Surrogate" }
        |]

    [<Test>]
    member __.UnpairedLowSurrogateBeginning () =
        check (String([|char 0xDD2F; char 0x61|])) [|
            { Codepoint = 0xDD2F; Name = "Low Surrogate" }
            { Codepoint = 0x61; Name = "LATIN SMALL LETTER A" }
        |]

    [<Test>]
    member __.UnpairedHighSurrogateOnly () =
        check (String([|char 0xD83E|])) [|
            { Codepoint = 0xD83E; Name = "Non Private Use High Surrogate" }
        |]

    [<Test>]
    member __.UnpairedHighSurrogateBeginning () =
        check (String([|char 0xD83E; char 0x61|])) [|
            { Codepoint = 0xD83E; Name = "Non Private Use High Surrogate" }
            { Codepoint = 0x61; Name = "LATIN SMALL LETTER A" }
        |]
