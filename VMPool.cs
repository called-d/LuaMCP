namespace LuaMCP {
    public class VMPool : IDisposable
    {
        private readonly Dictionary<string, LuaEngine> _luaEngines = [];

        public string PrepareId(string? id) => id switch {
            string s when s.Trim().Length > 0 => s,
            _ => Guid.NewGuid().ToString() // whitespace/empty string or null
        };
        public LuaEngine GetOrCreate(string name)
        {
            if (_luaEngines.TryGetValue(name, out var engine)) return engine;
            return _luaEngines[name] = new LuaEngine();
        }

        public void Dispose()
        {
            foreach (var engine in _luaEngines.Values) engine.Dispose();
            _luaEngines.Clear();
        }
    }
}
