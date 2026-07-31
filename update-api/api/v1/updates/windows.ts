import type { VercelRequest, VercelResponse } from "@vercel/node";
import { loadLatestRelease } from "../../../lib/releases.js";
import {
  compareVersions,
  isInstallationId,
  isInRollout,
  parseVersion,
  selectArtifact,
  stableChannel,
  windowsX64Rid,
} from "../../../lib/protocol.js";
import { expiration, signDownload } from "../../../lib/r2.js";

export default async function handler(request: VercelRequest, response: VercelResponse) {
  response.setHeader("Cache-Control", "no-store");
  if (request.method !== "GET") return response.status(405).end();

  let currentVersion: string;
  let installationId: string;
  try {
    currentVersion = query(request, "currentVersion");
    const channel = query(request, "channel");
    const rid = query(request, "rid");
    installationId = query(request, "installationId");
    parseVersion(currentVersion);
    if (channel !== stableChannel || rid !== windowsX64Rid
        || !isInstallationId(installationId)) {
      throw new Error("Invalid update request");
    }
  } catch {
    return response.status(400).json({ error: "Invalid update request" });
  }

  try {
    const { envelope, release } = await loadLatestRelease();
    if (compareVersions(currentVersion, release.version) >= 0) {
      return response.status(204).end();
    }
    const mandatory = compareVersions(currentVersion,
      release.minimumSupportedVersion) < 0;
    if (!mandatory && !isInRollout(installationId, release.version,
      release.rolloutPercentage)) {
      return response.status(204).end();
    }

    const selected = selectArtifact(release, currentVersion);
    const [manifestUrl, packageUrl, fullPackageUrl, bootstrapperUrl] =
      await Promise.all([
        signDownload(release.targetManifest.objectKey),
        signDownload(selected.objectKey),
        signDownload(release.fullPackage.objectKey),
        release.bootstrapper ? signDownload(release.bootstrapper.objectKey) : undefined,
      ]);
    return response.status(200).json({
      release: envelope,
      selectedArtifactId: selected.id,
      manifestUrl,
      packageUrl,
      fullPackageUrl,
      bootstrapperUrl,
      expiresAt: expiration(),
    });
  } catch (error) {
    console.error("Update request failed", error instanceof Error ? error.message : error);
    return response.status(503).json({ error: "Update service unavailable" });
  }
}

function query(request: VercelRequest, name: string): string {
  const value = request.query[name];
  if (typeof value !== "string" || value.length > 128) {
    throw new Error(`Invalid query parameter ${name}`);
  }
  return value;
}
