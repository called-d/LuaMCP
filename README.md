# LuaMCP
MCP の理解のために書いた、シンプルな Lua 実行環境です。外部に悪さできないように一通りの手段は塞いであります [^jail]

## Tools
### EvalLuaCode
* `string code`
* `string? sessionId = null`

`code` を実行して結果を返します。関数の定義など、グローバル変数に格納したものを後から使うことができます

`sessionId` はそれぞれの Lua のステートに振られる識別子です。省略すると新しい独立したステートを作成し一意な `sessionId` を与えます

それぞれのステートは永続化されていません。MCP サーバーの終了時に揮発します

> [!NOTE]
> `code` が Lua のチャンクとして正しい必要があるため、`return 1 + 1` や `do for k, _ in pairs(_G) do print(k) end end` のようにする必要があります（だいたい指示しなくてもこうしてくれますが）

### ListGlobals
* `string? sessionId = null`
* `bool? includeMisc = false`

ステートのグローバル変数のリストを返します
ライブラリのテーブルや `_G`, `_VERSION` 等は表示しないことができます

## 設定
Visual Studio Code の場合

設定で `Chat › Mcp: Enabled` にして `LuaMCP` を settings.json に追加してください
```json:settings.json
    "mcp": {
        "inputs": [],
        "servers": {
            "LuaMCP": {
                "command": "path\\to\\LuaMCP.exe",
            }
        }
    }
```

[^jail]: LuaEngine.cs の `#region limitations` 周辺
