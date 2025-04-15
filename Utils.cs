namespace LuaMCP {
    public static class Utils {

        /// <summary>
        /// path が rootDir 以下のパスかどうかを調べる
        /// </summary>
        /// <param name="rootDir">完全修飾パス</param>
        /// <param name="path"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public static bool IsInDirectory(string rootDir, string path, out string? error) {
            var fullPath = Path.GetFullPath(path, rootDir);
            DirectoryInfo? d = File.Exists(fullPath)
                ? new FileInfo(fullPath).Directory
                : Directory.GetParent(fullPath);
            if (d == null) {
                error = "directory is not exist";
                return false;
            }
            while (d != null) {
                if (d.FullName == rootDir) {
                    error = null;
                    return true;
                }
                d = d.Parent;
            }
            error = null;
            return false;
        }
    }
}
