namespace LuaMCP {
    public class VMPool : IDisposable
    {
        private readonly Dictionary<string, LuaEngine> _luaEngines = [];

        public LuaEngine GetOrCreate(string? name, out string name_)
        {
            if (name == "") name = null;
            name_ = name ??= Guid.NewGuid().ToString();
            if (_luaEngines.TryGetValue(name, out var engine)) return engine;

            var newEngine = new LuaEngine();
            _luaEngines[name] = newEngine;
            return newEngine;
        }

        public void Dispose()
        {
            foreach (var engine in _luaEngines.Values) engine.Dispose();
            _luaEngines.Clear();
        }
    }
}
