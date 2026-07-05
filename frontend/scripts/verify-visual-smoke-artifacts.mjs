import { verifyVisualSmokeManifest } from "./visual-smoke-artifacts.mjs";

function arg(name, fallback) {
  const prefix = `--${name}=`;
  const value = process.argv.find((item) => item.startsWith(prefix));
  return value ? value.slice(prefix.length) : process.env[name.toUpperCase().replaceAll("-", "_")] ?? fallback;
}

const manifestPath = arg("manifest", "artifacts/visual-smoke/visual-smoke-manifest.json");

verifyVisualSmokeManifest(manifestPath)
  .then((result) => {
    console.log(JSON.stringify(result, null, 2));
  })
  .catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
