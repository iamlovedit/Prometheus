import type { VercelRequest, VercelResponse } from "@vercel/node";
import { loadLatestRelease } from "../../../lib/releases.js";
import { stableChannel, windowsX64Rid } from "../../../lib/protocol.js";
import { signDownload } from "../../../lib/r2.js";

export default async function handler(request: VercelRequest, response: VercelResponse) {
  response.setHeader("Cache-Control", "no-store");
  if (request.method !== "GET") return response.status(405).end();
  try {
    if (request.query.channel !== stableChannel || request.query.rid !== windowsX64Rid) {
      return response.status(400).json({ error: "Invalid download request" });
    }
    const { release } = await loadLatestRelease();
    return response.redirect(302, await signDownload(release.portablePackage.objectKey));
  } catch (error) {
    console.error("Portable download failed", error instanceof Error ? error.message : error);
    return response.status(503).json({ error: "Download service unavailable" });
  }
}
