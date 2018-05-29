namespace CodepointsPlz.Shared

open System

type DesiredAction =
    | Reply = 1
    | Quote = 2
    | Tweet = 3

type CodepointsPlzMention = 
    { StatusID : uint64
      DirectTrigger : bool
      DesiredAction : DesiredAction }

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

[<CLIMutable>]
type Reply =
    { Mention : Mention
      TargetTweetEmbedHtml : string
      TargetText : string
      Codepoints : Codepoint[]
      TargetUserScreenName : string
      TargetUserDisplayName : string
      TargetUserSummary : string
      UserScreenNameCodepoints : Codepoint[]
      UserDisplayNameCodepoints : Codepoint[]
      UserSummaryCodepoints : Codepoint[] }

type CodepointRequest =
    | PlainText of text : string
    | Tweet of id : uint64
    | User of id : uint64
    | Blank

type WebData = 
    { Codepoints : Codepoint[]
      MentionEmbedHtml : string
      Text : string
      TargetEmbedHtml : string
      ScreenName : string
      DisplayName : string
      Summary : string
      ScreenNameCodepoints : Codepoint[]
      DisplayNameCodepoints : Codepoint[]
      SummaryCodepoints : Codepoint[] }