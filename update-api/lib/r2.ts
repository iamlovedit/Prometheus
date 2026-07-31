import { GetObjectCommand, S3Client } from "@aws-sdk/client-s3";
import { getSignedUrl } from "@aws-sdk/s3-request-presigner";
import type { SignedEnvelope } from "./types.js";

const expiresInSeconds = 6 * 60 * 60;

function config() {
  const accountId = required("R2_ACCOUNT_ID");
  return {
    bucket: required("R2_BUCKET"),
    client: new S3Client({
      region: "auto",
      endpoint: `https://${accountId}.r2.cloudflarestorage.com`,
      forcePathStyle: true,
      credentials: {
        accessKeyId: required("R2_ACCESS_KEY_ID"),
        secretAccessKey: required("R2_SECRET_ACCESS_KEY"),
      },
    }),
  };
}

export function publicKey(): string {
  return required("UPDATE_SIGNING_PUBLIC_KEY_BASE64");
}

export async function readEnvelope(objectKey: string): Promise<SignedEnvelope> {
  const { bucket, client } = config();
  const response = await client.send(new GetObjectCommand({ Bucket: bucket, Key: objectKey }));
  const text = await response.Body?.transformToString("utf8");
  if (!text) throw new Error(`R2 object is empty: ${objectKey}`);
  return JSON.parse(text) as SignedEnvelope;
}

export async function signDownload(objectKey: string): Promise<string> {
  const { bucket, client } = config();
  return getSignedUrl(client, new GetObjectCommand({ Bucket: bucket, Key: objectKey }), {
    expiresIn: expiresInSeconds,
  });
}

export function expiration(): string {
  return new Date(Date.now() + expiresInSeconds * 1000).toISOString();
}

function required(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`Missing environment variable ${name}`);
  return value;
}
