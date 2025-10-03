using Wormhole.Sync.Extensions;
using Wormhole.Sync.Serialization;
#if NET48
using System.Web.SessionState;
#else
using Microsoft.AspNetCore.Http;
#endif

namespace Wormhole.Sync.Web.Server
{
    /// <summary>
    /// Session extensions.
    /// </summary>
    public static class SessionExtensions
    {
        private static ISerializer serializer = SerializersFactory.JsonSerializerFactory.GetSerializer();

#if NET48
        /// <summary>
        /// Get a value from the session.
        /// </summary>
        public static T Get<T>(this HttpSessionState session, string key)
        {
            var data = session[key] as string;

            return data == null ? default : serializer.Deserialize<T>(data);
        }

        /// <summary>
        /// Set a value in the session.
        /// </summary>
        public static void Set<T>(this HttpSessionState session, string key, T value)
        {
            var jsonBytes = serializer.Serialize(value);

            session[key] = jsonBytes.ToUtf8String();
        }

        /// <summary>
        /// Get a string value from the session.
        /// </summary>
        public static string GetString(this HttpSessionState session, string key)
        {
            return session[key] as string;
        }

        /// <summary>
        /// Set a string value in the session.
        /// </summary>
        public static void SetString(this HttpSessionState session, string key, string value)
        {
            session[key] = value;
        }

        /// <summary>
        /// Get a value from the session (HttpSessionStateBase overload).
        /// </summary>
        public static T Get<T>(this System.Web.HttpSessionStateBase session, string key)
        {
            var data = session[key] as string;

            return data == null ? default : serializer.Deserialize<T>(data);
        }

        /// <summary>
        /// Set a value in the session (HttpSessionStateBase overload).
        /// </summary>
        public static void Set<T>(this System.Web.HttpSessionStateBase session, string key, T value)
        {
            var jsonBytes = serializer.Serialize(value);

            session[key] = jsonBytes.ToUtf8String();
        }

        /// <summary>
        /// Get a string value from the session (HttpSessionStateBase overload).
        /// </summary>
        public static string GetString(this System.Web.HttpSessionStateBase session, string key)
        {
            return session[key] as string;
        }

        /// <summary>
        /// Set a string value in the session (HttpSessionStateBase overload).
        /// </summary>
        public static void SetString(this System.Web.HttpSessionStateBase session, string key, string value)
        {
            session[key] = value;
        }
#else
        /// <summary>
        /// Get a value from the session.
        /// </summary>
        public static T Get<T>(this ISession session, string key)
        {
            var data = session.GetString(key);

            return data == null ? default : serializer.Deserialize<T>(data);
        }

        /// <summary>
        /// Set a value in the session.
        /// </summary>
        public static void Set<T>(this ISession session, string key, T value)
        {
            var jsonBytes = serializer.Serialize(value);

            session.SetString(key, jsonBytes.ToUtf8String());
        }
#endif
    }
}