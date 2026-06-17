// HorizonAppleSignIn.mm
//
// Objective-C++ bridge between the horizOn Unity SDK (managed C# side:
// HorizonAppleSignInBridge.cs) and Apple's AuthenticationServices framework.
//
// Exposes a single C entry point _HorizonAppleSignIn_Present which is invoked
// from C# via [DllImport("__Internal")]. The result is delivered back to the
// Unity GameObject "HorizonAppleSignInBridge" (created by the C# bridge) by
// invoking its "OnAppleSignInResult" method with a JSON payload.
//
// JSON payload shape:
//   { "identityToken": "...", "firstName": "...", "lastName": "...", "error": "..." }
//
// On success: identityToken is populated; firstName / lastName may be empty for
// non-first-login. "error" is empty.
// On failure: "error" is one of the documented horizOn error codes
// (INVALID_APPLE_TOKEN, APPLE_NOT_CONFIGURED, NETWORK_ERROR) or an Apple
// ASAuthorizationError name that the C# side maps.

#import <Foundation/Foundation.h>
#import <AuthenticationServices/AuthenticationServices.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static NSString* const kHorizonAppleBridgeGameObject = @"HorizonAppleSignInBridge";
static NSString* const kHorizonAppleBridgeMethod     = @"OnAppleSignInResult";

API_AVAILABLE(ios(13.0))
@interface HorizonAppleSignInDelegate : NSObject <ASAuthorizationControllerDelegate, ASAuthorizationControllerPresentationContextProviding>
@end

// Strong reference to the in-flight delegate so it survives until the callback
// fires. ASAuthorizationController only retains its delegate weakly.
static HorizonAppleSignInDelegate* gHorizonAppleSignInDelegate = nil;

@implementation HorizonAppleSignInDelegate

- (void)startWithNonce:(NSString*)nonce
{
    if (@available(iOS 13.0, *))
    {
        ASAuthorizationAppleIDProvider* provider = [[ASAuthorizationAppleIDProvider alloc] init];
        ASAuthorizationAppleIDRequest* request = [provider createRequest];
        request.requestedScopes = @[ ASAuthorizationScopeFullName, ASAuthorizationScopeEmail ];
        if (nonce.length > 0)
        {
            request.nonce = nonce;
        }

        ASAuthorizationController* controller = [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[ request ]];
        controller.delegate = self;
        controller.presentationContextProvider = self;
        [controller performRequests];
    }
    else
    {
        [self deliverErrorCode:@"APPLE_NOT_CONFIGURED"];
    }
}

- (void)deliverPayload:(NSDictionary*)payload
{
    NSError* err = nil;
    NSData* data = [NSJSONSerialization dataWithJSONObject:payload options:0 error:&err];
    NSString* json = data ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : @"{\"error\":\"INVALID_APPLE_TOKEN\"}";

    UnitySendMessage([kHorizonAppleBridgeGameObject UTF8String],
                     [kHorizonAppleBridgeMethod UTF8String],
                     [json UTF8String]);

    // Release ourselves now that the callback has fired.
    gHorizonAppleSignInDelegate = nil;
}

- (void)deliverErrorCode:(NSString*)code
{
    [self deliverPayload:@{ @"error": code ?: @"INVALID_APPLE_TOKEN" }];
}

#pragma mark - ASAuthorizationControllerDelegate

- (void)authorizationController:(ASAuthorizationController*)controller
   didCompleteWithAuthorization:(ASAuthorization*)authorization
API_AVAILABLE(ios(13.0))
{
    if (![authorization.credential isKindOfClass:[ASAuthorizationAppleIDCredential class]])
    {
        [self deliverErrorCode:@"INVALID_APPLE_TOKEN"];
        return;
    }

    ASAuthorizationAppleIDCredential* cred = (ASAuthorizationAppleIDCredential*)authorization.credential;
    NSString* idToken = [[NSString alloc] initWithData:cred.identityToken encoding:NSUTF8StringEncoding];
    if (idToken.length == 0)
    {
        [self deliverErrorCode:@"INVALID_APPLE_TOKEN"];
        return;
    }

    NSString* firstName = cred.fullName.givenName ?: @"";
    NSString* lastName  = cred.fullName.familyName ?: @"";

    [self deliverPayload:@{
        @"identityToken": idToken,
        @"firstName":     firstName,
        @"lastName":      lastName,
        @"error":         @""
    }];
}

- (void)authorizationController:(ASAuthorizationController*)controller
           didCompleteWithError:(NSError*)error
API_AVAILABLE(ios(13.0))
{
    NSString* code = @"INVALID_APPLE_TOKEN";
    if (error.domain == ASAuthorizationErrorDomain)
    {
        switch ((ASAuthorizationError)error.code)
        {
            case ASAuthorizationErrorNotHandled:
            case ASAuthorizationErrorNotInteractive:
                code = @"APPLE_NOT_CONFIGURED";
                break;
            case ASAuthorizationErrorFailed:
            case ASAuthorizationErrorInvalidResponse:
            case ASAuthorizationErrorUnknown:
                code = @"NETWORK_ERROR";
                break;
            case ASAuthorizationErrorCanceled:
            default:
                code = @"INVALID_APPLE_TOKEN";
                break;
        }
    }
    [self deliverErrorCode:code];
}

#pragma mark - ASAuthorizationControllerPresentationContextProviding

- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController*)controller
API_AVAILABLE(ios(13.0))
{
    UIWindow* keyWindow = nil;
    NSArray<UIWindow*>* windows = [UIApplication sharedApplication].windows;
    for (UIWindow* w in windows)
    {
        if (w.isKeyWindow)
        {
            keyWindow = w;
            break;
        }
    }
    return keyWindow ?: windows.firstObject;
}

@end

extern "C" {

void _HorizonAppleSignIn_Present(const char* nonce)
{
    if (@available(iOS 13.0, *))
    {
        NSString* nonceStr = nonce ? [NSString stringWithUTF8String:nonce] : @"";
        gHorizonAppleSignInDelegate = [[HorizonAppleSignInDelegate alloc] init];
        [gHorizonAppleSignInDelegate startWithNonce:nonceStr];
    }
    else
    {
        NSString* json = @"{\"error\":\"APPLE_NOT_CONFIGURED\"}";
        UnitySendMessage([kHorizonAppleBridgeGameObject UTF8String],
                         [kHorizonAppleBridgeMethod UTF8String],
                         [json UTF8String]);
    }
}

}
