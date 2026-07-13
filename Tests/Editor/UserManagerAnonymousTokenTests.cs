using NUnit.Framework;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Tests
{
    /// <summary>
    /// TASK-451: the signin response does not echo the anonymous token, so the
    /// SDK must keep the token it sent itself. Otherwise every session restore
    /// wipes CurrentUser.AnonymousToken, and a recovery token used on a new
    /// device is never persisted there - the recovery lasts exactly one session.
    /// </summary>
    public class UserManagerAnonymousTokenTests
    {
        private const string SentToken = "aaaabbbbccccddddeeeeffff00001111";
        private const string EchoedToken = "11110000ffffeeeeddddccccbbbbaaaa";

        [Test]
        public void SignInWithoutEchoedToken_KeepsTheTokenTheClientSent()
        {
            Assert.AreEqual(SentToken,
                UserManager.ResolveAnonymousToken(true, null, SentToken));
            Assert.AreEqual(SentToken,
                UserManager.ResolveAnonymousToken(true, string.Empty, SentToken));
        }

        [Test]
        public void SignUpWithEchoedToken_PrefersTheServerToken()
        {
            Assert.AreEqual(EchoedToken,
                UserManager.ResolveAnonymousToken(true, EchoedToken, SentToken));
        }

        [Test]
        public void NonAnonymousUser_GetsNoAnonymousToken()
        {
            Assert.AreEqual(string.Empty,
                UserManager.ResolveAnonymousToken(false, EchoedToken, SentToken));
        }

        [Test]
        public void NoTokenAnywhere_ResolvesToEmptyInsteadOfNull()
        {
            Assert.AreEqual(string.Empty,
                UserManager.ResolveAnonymousToken(true, null, null));
        }
    }
}
