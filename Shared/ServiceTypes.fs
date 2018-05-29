namespace CodepointsPlz.Shared

open System

type TriggerMention = 
    { StatusID : uint64
      DirectTrigger : bool }

[<CLIMutable>]
type UserMention =
    { UserID : uint64
      ScreenName : string }

[<CLIMutable>]
type Mention =
    { Text : string
      UserMentions : UserMention[]
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