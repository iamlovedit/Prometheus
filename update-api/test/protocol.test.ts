import { describe, expect, test } from "vitest";
import {
  compareVersions,
  isInstallationId,
  isInRollout,
  parseVersion,
  selectArtifact,
  validateRelease,
} from "../lib/protocol.js";
import type { ReleaseDescriptor } from "../lib/types.js";

const release: ReleaseDescriptor = {
  schemaVersion: 1,
  channel: "stable",
  rid: "win-x64",
  version: "1.2.0",
  minimumSupportedVersion: "1.0.0",
  minimumBootstrapperVersion: "1.0.0",
  bootstrapperVersion: "1.2.0",
  rolloutPercentage: 100,
  publishedAt: new Date().toISOString(),
  releaseNotes: {},
  targetManifest: { id: "manifest", objectKey: "releases/1.2.0/win-x64/manifest.json", size: 1, sha256: "a".repeat(64) },
  fullPackage: { id: "full", objectKey: "releases/1.2.0/win-x64/full.zip", size: 1000, sha256: "b".repeat(64) },
  portablePackage: { id: "portable", objectKey: "releases/1.2.0/win-x64/portable.zip", size: 1100, sha256: "c".repeat(64) },
  deltas: [{
    id: "delta:1.1.0",
    baseVersion: "1.1.0",
    objectKey: "releases/1.2.0/win-x64/deltas/from-1.1.0.zip",
    size: 200,
    sha256: "d".repeat(64),
  }],
};

describe("version protocol", () => {
  test("compares stable versions", () => {
    expect(compareVersions("1.2.0", "1.1.9")).toBeGreaterThan(0);
    expect(compareVersions("1.2.0", "1.2.0")).toBe(0);
  });

  test("rejects prerelease and malformed versions", () => {
    expect(() => parseVersion("1.2.0-beta")).toThrow();
    expect(() => parseVersion("1.2")).toThrow();
    expect(() => parseVersion("01.2.3")).toThrow();
    expect(() => parseVersion("2147483648.0.0")).toThrow();
  });

  test("validates installation IDs exactly", () => {
    expect(isInstallationId("01234567-89ab-cdef-0123-456789abcdef")).toBe(true);
    expect(isInstallationId("01234567------------------------------------")).toBe(false);
  });
});

describe("release selection", () => {
  test("selects an exact, small direct delta", () => {
    expect(selectArtifact(release, "1.1.0").id).toBe("delta:1.1.0");
  });

  test("falls back to the full package", () => {
    expect(selectArtifact(release, "1.0.0").id).toBe("full");
  });

  test("rollout is deterministic", () => {
    const id = "01234567-89ab-cdef-0123-456789abcdef";
    expect(isInRollout(id, "1.2.0", 50)).toBe(isInRollout(id, "1.2.0", 50));
    expect(isInRollout(id, "1.2.0", 100)).toBe(true);
    expect(isInRollout(id, "1.2.0", 0)).toBe(false);
  });

  test("rejects an object outside the signed release prefix", () => {
    expect(() => validateRelease(release)).not.toThrow();
    const invalid = structuredClone(release);
    invalid.fullPackage.objectKey = "other/full.zip";
    expect(() => validateRelease(invalid)).toThrow();
  });
});
