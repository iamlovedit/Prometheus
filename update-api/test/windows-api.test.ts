import type { VercelRequest, VercelResponse } from "@vercel/node";
import { beforeEach, describe, expect, test, vi } from "vitest";
import downloadHandler from "../api/v1/downloads/windows.js";
import updateHandler from "../api/v1/updates/windows.js";
import { loadLatestRelease } from "../lib/releases.js";
import { signDownload } from "../lib/r2.js";
import type { ReleaseDescriptor, SignedEnvelope } from "../lib/types.js";

vi.mock("../lib/releases.js", () => ({
  loadLatestRelease: vi.fn(),
}));

vi.mock("../lib/r2.js", () => ({
  expiration: () => "2030-01-01T06:00:00.000Z",
  signDownload: vi.fn((key: string) => Promise.resolve(`https://r2.example/${key}`)),
}));

const envelope: SignedEnvelope = { payload: "payload", signature: "signature" };
const release: ReleaseDescriptor = {
  schemaVersion: 1,
  channel: "stable",
  rid: "win-x64",
  version: "1.1.0",
  minimumSupportedVersion: "1.0.0",
  minimumBootstrapperVersion: "1.0.0",
  bootstrapperVersion: "1.1.0",
  rolloutPercentage: 100,
  publishedAt: "2030-01-01T00:00:00.000Z",
  releaseNotes: {},
  targetManifest: artifact("manifest", "manifest.json", 10, "a"),
  fullPackage: artifact("full", "full.zip", 100, "b"),
  portablePackage: artifact("portable", "portable.zip", 120, "c"),
  bootstrapper: artifact("bootstrapper", "Prometheus.exe", 20, "d"),
  deltas: [{
    ...artifact("delta:1.0.0", "deltas/from-1.0.0.zip", 20, "e"),
    baseVersion: "1.0.0",
  }],
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(loadLatestRelease).mockResolvedValue({ envelope, release });
});

describe("windows update API", () => {
  test("returns 400 before reading R2 for invalid parameters", async () => {
    const response = fakeResponse();

    await updateHandler(request({
      currentVersion: "1.0",
      channel: "stable",
      rid: "win-x64",
      installationId: "invalid",
    }), response.value);

    expect(response.statusCode).toBe(400);
    expect(loadLatestRelease).not.toHaveBeenCalled();
  });

  test("returns 204 when the client is current", async () => {
    const response = fakeResponse();

    await updateHandler(request({
      currentVersion: "1.1.0",
      channel: "stable",
      rid: "win-x64",
      installationId: "01234567-89ab-cdef-0123-456789abcdef",
    }), response.value);

    expect(response.statusCode).toBe(204);
  });

  test("selects only the signed direct delta and fallback objects", async () => {
    const response = fakeResponse();

    await updateHandler(request({
      currentVersion: "1.0.0",
      channel: "stable",
      rid: "win-x64",
      installationId: "01234567-89ab-cdef-0123-456789abcdef",
    }), response.value);

    expect(response.statusCode).toBe(200);
    expect(response.body.selectedArtifactId).toBe("delta:1.0.0");
    expect(vi.mocked(signDownload).mock.calls.map(([key]) => key)).toEqual([
      release.targetManifest.objectKey,
      release.deltas[0].objectKey,
      release.fullPackage.objectKey,
      release.bootstrapper?.objectKey,
    ]);
  });

  test("returns 503 without exposing private R2 failures", async () => {
    vi.mocked(loadLatestRelease).mockRejectedValue(new Error("private bucket detail"));
    const response = fakeResponse();

    await updateHandler(request({
      currentVersion: "1.0.0",
      channel: "stable",
      rid: "win-x64",
      installationId: "01234567-89ab-cdef-0123-456789abcdef",
    }), response.value);

    expect(response.statusCode).toBe(503);
    expect(response.body).toEqual({ error: "Update service unavailable" });
  });
});

describe("portable download API", () => {
  test("rejects unsupported channel and rid", async () => {
    const response = fakeResponse();

    await downloadHandler(request({ channel: "beta", rid: "win-arm64" }),
      response.value);

    expect(response.statusCode).toBe(400);
    expect(signDownload).not.toHaveBeenCalled();
  });

  test("redirects only to the signed portable package", async () => {
    const response = fakeResponse();

    await downloadHandler(request({ channel: "stable", rid: "win-x64" }),
      response.value);

    expect(signDownload).toHaveBeenCalledWith(release.portablePackage.objectKey);
    expect(response.statusCode).toBe(302);
  });
});

function artifact(id: string, name: string, size: number, hash: string) {
  return {
    id,
    objectKey: `releases/1.1.0/win-x64/${name}`,
    size,
    sha256: hash.repeat(64),
  };
}

function request(query: Record<string, string>): VercelRequest {
  return { method: "GET", query } as unknown as VercelRequest;
}

function fakeResponse() {
  const state: { statusCode: number; body: any } = { statusCode: 200, body: undefined };
  let value: VercelResponse;
  const implementation = {
    setHeader: vi.fn(),
    status: vi.fn((code: number) => {
      state.statusCode = code;
      return value;
    }),
    json: vi.fn((body: unknown) => {
      state.body = body;
      return value;
    }),
    end: vi.fn(() => value),
    redirect: vi.fn((code: number, location: string) => {
      state.statusCode = code;
      state.body = location;
      return value;
    }),
  };
  value = implementation as unknown as VercelResponse;
  return {
    value,
    get statusCode() { return state.statusCode; },
    get body() { return state.body; },
  };
}
