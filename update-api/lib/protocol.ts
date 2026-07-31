import { createHash, createPublicKey, verify } from "node:crypto";
import type {
  ChannelIndex,
  DeltaArtifact,
  ReleaseDescriptor,
  SignedEnvelope,
  UpdateArtifact,
} from "./types.js";

export const protocolVersion = 1;
export const stableChannel = "stable";
export const windowsX64Rid = "win-x64";

export function verifyEnvelope<T>(
  envelope: SignedEnvelope,
  publicKeyBase64: string,
): T {
  const payload = Buffer.from(envelope.payload, "base64url");
  const signature = Buffer.from(envelope.signature, "base64url");
  const publicKey = createPublicKey({
    key: Buffer.from(publicKeyBase64, "base64"),
    format: "der",
    type: "spki",
  });
  const valid = verify("sha256", payload, {
    key: publicKey,
    dsaEncoding: "ieee-p1363",
  }, signature);
  if (!valid) {
    throw new Error("Update signature verification failed");
  }
  return JSON.parse(payload.toString("utf8")) as T;
}

export function parseVersion(value: string): readonly [number, number, number] {
  const match = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.exec(value);
  if (!match) {
    throw new Error("Invalid version");
  }
  const parts = [Number(match[1]), Number(match[2]), Number(match[3])] as const;
  if (parts.some((part) => !Number.isSafeInteger(part) || part > 2_147_483_647)) {
    throw new Error("Invalid version");
  }
  return parts;
}

export function isInstallationId(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
    .test(value);
}

export function compareVersions(left: string, right: string): number {
  const a = parseVersion(left);
  const b = parseVersion(right);
  for (let index = 0; index < 3; index += 1) {
    if (a[index] !== b[index]) {
      return a[index] - b[index];
    }
  }
  return 0;
}

export function isInRollout(
  installationId: string,
  version: string,
  percentage: number,
): boolean {
  if (percentage >= 100) return true;
  if (percentage <= 0) return false;
  const digest = createHash("sha256")
    .update(`${installationId}:${version}`, "utf8")
    .digest();
  return digest.readUInt32BE(0) % 100 < percentage;
}

export function selectArtifact(
  release: ReleaseDescriptor,
  currentVersion: string,
): UpdateArtifact | DeltaArtifact {
  const delta = release.deltas.find((item) =>
    item.baseVersion === currentVersion
    && item.size * 100 < release.fullPackage.size * 70
  );
  return delta ?? release.fullPackage;
}

export function validateChannel(index: ChannelIndex): void {
  if (index.schemaVersion !== protocolVersion
      || index.channel !== stableChannel
      || index.rid !== windowsX64Rid
      || index.releases.length === 0) {
    throw new Error("Invalid stable channel index");
  }
  const versions = new Set<string>();
  let previousVersion: string | undefined;
  for (const release of index.releases) {
    parseVersion(release.version);
    const prefix = `releases/${release.version}/${windowsX64Rid}/`;
    if (versions.has(release.version)
        || previousVersion !== undefined
        && compareVersions(previousVersion, release.version) <= 0
        || release.releaseObjectKey !== `${prefix}release.json`
        || release.manifestObjectKey !== `${prefix}manifest.json`
        || !Number.isFinite(Date.parse(release.publishedAt))) {
      throw new Error("Invalid stable channel release");
    }
    versions.add(release.version);
    previousVersion = release.version;
  }
}

export function validateRelease(release: ReleaseDescriptor): void {
  parseVersion(release.version);
  parseVersion(release.minimumSupportedVersion);
  parseVersion(release.minimumBootstrapperVersion);
  parseVersion(release.bootstrapperVersion);
  if (release.schemaVersion !== protocolVersion
      || release.channel !== stableChannel
      || release.rid !== windowsX64Rid
      || release.rolloutPercentage < 0
      || release.rolloutPercentage > 100
      || !Number.isFinite(Date.parse(release.publishedAt))
      || compareVersions(release.minimumSupportedVersion, release.version) > 0
      || compareVersions(release.minimumBootstrapperVersion,
        release.bootstrapperVersion) > 0) {
    throw new Error("Invalid release descriptor");
  }
  const prefix = `releases/${release.version}/${windowsX64Rid}/`;
  const artifacts = [
    release.targetManifest,
    release.fullPackage,
    release.portablePackage,
    release.bootstrapper,
    ...release.deltas,
  ].filter((value): value is UpdateArtifact | DeltaArtifact => value !== undefined);
  const ids = new Set<string>();
  for (const artifact of artifacts) {
    if (!artifact.objectKey.startsWith(prefix)
        || artifact.objectKey.split("/").some((part) => !part || part === "." || part === "..")
        || artifact.size <= 0
        || !/^[a-f0-9]{64}$/i.test(artifact.sha256)
        || !artifact.id
        || ids.has(artifact.id)) {
      throw new Error("Release contains an invalid artifact");
    }
    ids.add(artifact.id);
  }
  if (release.targetManifest.id !== "manifest"
      || release.fullPackage.id !== "full"
      || release.portablePackage.id !== "portable"
      || release.bootstrapper && release.bootstrapper.id !== "bootstrapper") {
    throw new Error("Release contains an invalid artifact ID");
  }
  for (const delta of release.deltas) {
    parseVersion(delta.baseVersion);
    if (delta.id !== `delta:${delta.baseVersion}`
        || compareVersions(delta.baseVersion, release.version) >= 0) {
      throw new Error("Release contains an invalid delta");
    }
  }
}
