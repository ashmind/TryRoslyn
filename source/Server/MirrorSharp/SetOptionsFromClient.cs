﻿using MirrorSharp.Advanced;

namespace TryRoslyn.Server.MirrorSharp {
    public class SetOptionsFromClient : ISetOptionsFromClientExtension {
        private const string TargetLanguage = "x-target-language";

        public bool TrySetOption(IWorkSession session, string name, string value) {
            if (name != TargetLanguage)
                return false;

            session.SetTargetLanguageName(value);
            return true;
        }
    }
}