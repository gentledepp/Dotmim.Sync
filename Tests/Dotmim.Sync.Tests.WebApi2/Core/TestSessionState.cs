using System;
using System.Web;

namespace Wormhole.Sync.Tests
{
    public class TestSessionState : HttpSessionStateBase
    {
        private readonly System.Collections.Generic.Dictionary<string, object> sessionData = new System.Collections.Generic.Dictionary<string, object>();
        private string sessionId = Guid.NewGuid().ToString();

        public override string SessionID => this.sessionId;

        public override void Clear()
        {
            this.sessionId = null;
            this.sessionData.Clear();
        }

        public override object this[string name]
        {
            get => this.sessionData.TryGetValue(name, out var value) ? value : null;
            set => this.sessionData[name] = value;
        }

        public override void Remove(string name)
        {
            this.sessionData.Remove(name);
        }
    }
}