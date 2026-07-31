import type { ChannelIndex, ReleaseDescriptor, SignedEnvelope } from "./types.js";
import {
  stableChannel,
  validateChannel,
  validateRelease,
  verifyEnvelope,
  windowsX64Rid,
} from "./protocol.js";
import { publicKey, readEnvelope } from "./r2.js";

export async function loadLatestRelease(): Promise<{
  envelope: SignedEnvelope;
  release: ReleaseDescriptor;
}> {
  const channelEnvelope = await readEnvelope(
    `channels/${stableChannel}/${windowsX64Rid}.json`,
  );
  const channel = verifyEnvelope<ChannelIndex>(channelEnvelope, publicKey());
  validateChannel(channel);
  const envelope = await readEnvelope(channel.releases[0].releaseObjectKey);
  const release = verifyEnvelope<ReleaseDescriptor>(envelope, publicKey());
  validateRelease(release);
  if (release.version !== channel.releases[0].version) {
    throw new Error("Channel and release versions do not match");
  }
  return { envelope, release };
}
