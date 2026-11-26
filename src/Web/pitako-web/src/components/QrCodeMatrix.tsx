"use client";

import QRCode from "qrcode";
import { useEffect, useState } from "react";

export function QrCodeMatrix({ value, size = 180, alt = "QR Code" }: { value: string; size?: number; alt?: string }) {
  const [src, setSrc] = useState<string>("");

  useEffect(() => {
    let active = true;
    if (!value) return;
    QRCode.toDataURL(value, { width: size, margin: 1, color: { dark: "#0f172a", light: "#ffffff" } })
      .then((url) => {
        if (active) setSrc(url);
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [value, size]);

  if (!src) {
    return <div style={{ width: size, height: size }} className="animate-pulse rounded-xl bg-slate-200" />;
  }

  return <img src={src} width={size} height={size} alt={alt} className="rounded-xl border border-slate-200 bg-white p-1" />;
}
