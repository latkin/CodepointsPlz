namespace CodepointsPlz.Shared

open System

type Settings =
    { BitlyAccessToken          : string
      ScreenshotApiKey          : string
      ScreenshotApiSecret       : string
      StorageConnectionString   : string 
      StorageTableName          : string
      TwitterAccessToken        : string
      TwitterAccessTokenSecret  : string
      TwitterApiKey             : string
      TwitterApiSecret          : string }
 
 module Settings =
    let load () = 
        let env s = Environment.GetEnvironmentVariable(s, EnvironmentVariableTarget.Process)

        { BitlyAccessToken          = env "bitlyaccesstoken"
          StorageConnectionString   = env "codepointsplz_STORAGE"
          StorageTableName          = env "storagetablename" 
          ScreenshotApiKey          = env "screenshotapikey"
          ScreenshotApiSecret       = env "screenshotapisecret" 
          TwitterAccessToken        = env "twitteraccesstoken"
          TwitterAccessTokenSecret  = env "twitteraccesstokensecret"
          TwitterApiKey             = env "twitterapikey"
          TwitterApiSecret          = env "twitterapisecret" }