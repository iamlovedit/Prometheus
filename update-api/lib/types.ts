export interface SignedEnvelope {
  payload: string;
  signature: string;
}

export interface UpdateArtifact {
  id: string;
  objectKey: string;
  size: number;
  sha256: string;
}

export interface DeltaArtifact extends UpdateArtifact {
  baseVersion: string;
}

export interface ReleaseDescriptor {
  schemaVersion: number;
  channel: string;
  rid: string;
  version: string;
  minimumSupportedVersion: string;
  minimumBootstrapperVersion: string;
  bootstrapperVersion: string;
  rolloutPercentage: number;
  publishedAt: string;
  releaseNotes: Record<string, string>;
  targetManifest: UpdateArtifact;
  fullPackage: UpdateArtifact;
  portablePackage: UpdateArtifact;
  bootstrapper?: UpdateArtifact;
  deltas: DeltaArtifact[];
}

export interface ChannelIndex {
  schemaVersion: number;
  channel: string;
  rid: string;
  releases: Array<{
    version: string;
    releaseObjectKey: string;
    manifestObjectKey: string;
    publishedAt: string;
  }>;
}
