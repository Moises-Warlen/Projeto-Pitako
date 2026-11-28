"use client";

import { Utensils } from "lucide-react";
import { useState } from "react";
import { resolveMediaUrl } from "@/lib/api";

export function formatBrl(value: number): string {
  return `R$ ${value.toFixed(2).replace(".", ",")}`;
}

export function ProductMediaFrame({
  src,
  alt,
  className = "h-28 w-28 rounded-xl",
  iconSize = 28,
  fit = "contain"
}: {
  src?: string | null;
  alt: string;
  className?: string;
  iconSize?: number;
  fit?: "contain" | "cover";
}) {
  const [failed, setFailed] = useState(false);
  const resolved = resolveMediaUrl(src);

  if (resolved && !failed) {
    return (
      <div
        className={`flex items-center justify-center overflow-hidden border border-slate-100 bg-white ${
          fit === "contain" ? "p-1" : ""
        } ${className}`}
      >
        <img
          src={resolved}
          alt={alt}
          loading="lazy"
          onError={() => setFailed(true)}
          className={`h-full w-full ${fit === "contain" ? "object-contain" : "object-cover"}`}
        />
      </div>
    );
  }

  return (
    <div
      aria-label={alt}
      className={`flex items-center justify-center border border-slate-100 bg-gradient-to-br from-slate-100 via-slate-50 to-slate-200 text-slate-400 ${className}`}
    >
      <Utensils style={{ width: iconSize, height: iconSize }} strokeWidth={1.6} />
    </div>
  );
}
