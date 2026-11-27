"use client";

let sharedAudioCtx: AudioContext | null = null;
let listenersAttached = false;

function getAudioContext(): AudioContext | null {
  if (typeof window === "undefined") return null;
  const Ctx = window.AudioContext || (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
  if (!Ctx) return null;
  if (!sharedAudioCtx) {
    sharedAudioCtx = new Ctx();
  }
  if (sharedAudioCtx.state === "suspended") {
    void sharedAudioCtx.resume().catch(() => {});
  }
  return sharedAudioCtx;
}

export function primeAlertAudio(): void {
  if (typeof window === "undefined" || listenersAttached) return;
  listenersAttached = true;
  const unlock = () => {
    const ctx = getAudioContext();
    if (ctx && ctx.state === "suspended") {
      void ctx.resume().catch(() => {});
    }
  };
  window.addEventListener("pointerdown", unlock, { passive: true });
  window.addEventListener("keydown", unlock, { passive: true });
}

function playToneSequence(notes: Array<{ freq: number; delayMs: number; durationMs: number; type?: OscillatorType; gain?: number }>) {
  try {
    const ctx = getAudioContext();
    if (!ctx) return;
    const baseTime = ctx.currentTime;
    for (const n of notes) {
      const osc = ctx.createOscillator();
      const gainNode = ctx.createGain();
      osc.type = n.type || "sine";
      osc.frequency.setValueAtTime(n.freq, baseTime + n.delayMs / 1000);
      gainNode.gain.setValueAtTime(0.001, baseTime + n.delayMs / 1000);
      gainNode.gain.exponentialRampToValueAtTime(n.gain ?? 0.22, baseTime + n.delayMs / 1000 + 0.02);
      gainNode.gain.exponentialRampToValueAtTime(0.001, baseTime + n.delayMs / 1000 + n.durationMs / 1000);
      osc.connect(gainNode);
      gainNode.connect(ctx.destination);
      osc.start(baseTime + n.delayMs / 1000);
      osc.stop(baseTime + n.delayMs / 1000 + n.durationMs / 1000 + 0.03);
    }
  } catch {
    // Silencioso caso bloqueado pelo navegador antes de interação
  }
}

export function playNewOrderSound(): void {
  playToneSequence([
    { freq: 659.25, delayMs: 0, durationMs: 160, type: "triangle", gain: 0.26 },
    { freq: 880, delayMs: 170, durationMs: 220, type: "triangle", gain: 0.28 },
    { freq: 1046.5, delayMs: 420, durationMs: 280, type: "triangle", gain: 0.28 }
  ]);
}

export function playNewMessageSound(): void {
  playToneSequence([
    { freq: 587.33, delayMs: 0, durationMs: 140, type: "sine", gain: 0.2 },
    { freq: 783.99, delayMs: 145, durationMs: 210, type: "sine", gain: 0.22 }
  ]);
}

export function playWaiterCallSound(): void {
  playToneSequence([
    { freq: 987.77, delayMs: 0, durationMs: 150, type: "square", gain: 0.14 },
    { freq: 1318.51, delayMs: 190, durationMs: 150, type: "square", gain: 0.14 },
    { freq: 987.77, delayMs: 390, durationMs: 240, type: "square", gain: 0.14 }
  ]);
}

export function playCustomerOrderUpdateSound(): void {
  playToneSequence([
    { freq: 523.25, delayMs: 0, durationMs: 140, type: "sine", gain: 0.2 },
    { freq: 659.25, delayMs: 150, durationMs: 160, type: "sine", gain: 0.22 },
    { freq: 783.99, delayMs: 320, durationMs: 240, type: "sine", gain: 0.24 }
  ]);
}
