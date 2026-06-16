// Auto-discovers tool modules in this directory.
// Each module must export: { name, description, inputSchema, requiresApproval, handler }
// Note: Unity C# side is the authoritative executor; these handlers are only used
// when the sidecar wants to handle a tool itself without round-tripping to Unity.
import { readdir } from "fs/promises";
import { pathToFileURL } from "url";
import { dirname, join } from "path";
import { fileURLToPath } from "url";

const __dirname = dirname(fileURLToPath(import.meta.url));

export async function loadTools() {
  const files = await readdir(__dirname);
  const tools = [];

  for (const file of files) {
    if (file === "index.js" || !file.endsWith(".js")) continue;
    try {
      const mod = await import(pathToFileURL(join(__dirname, file)).href);
      if (mod.default?.name) tools.push(mod.default);
    } catch (err) {
      console.warn(`[Tools] Failed to load ${file}: ${err.message}`);
    }
  }

  return tools;
}
