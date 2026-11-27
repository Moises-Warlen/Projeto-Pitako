"use client";

import * as signalR from "@microsoft/signalr";
import { useEffect, useRef, useState } from "react";
import { API_BASE_URL } from "./api";

export type RealtimeConnState = "disconnected" | "connecting" | "connected" | "reconnecting";

export interface PitakoRealtimeHandlers {
  onOrderUpdated?: (payload: unknown) => void;
  onWaiterCallUpdated?: (payload: unknown) => void;
  onWhatsappInboxUpdated?: (payload: unknown) => void;
  onCourierGpsUpdated?: (payload: unknown) => void;
}

/**
 * Conexão SignalR em tempo real com /hubs/realtime isolada pelo token JWT do tenant.
 */
export function usePitakoRealtime(
  accessToken: string | null | undefined,
  handlers: PitakoRealtimeHandlers
): RealtimeConnState {
  const [connState, setConnState] = useState<RealtimeConnState>("disconnected");
  const handlersRef = useRef(handlers);
  handlersRef.current = handlers;

  useEffect(() => {
    const token = accessToken?.trim();
    if (!token) {
      setConnState("disconnected");
      return;
    }

    let cancelled = false;
    setConnState("connecting");

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE_URL}/hubs/realtime`, {
        accessTokenFactory: () => token
      })
      .withAutomaticReconnect([0, 1500, 3000, 6000, 10000])
      .configureLogging(signalR.LogLevel.Error)
      .build();

    connection.on("orderUpdated", (payload) => {
      handlersRef.current.onOrderUpdated?.(payload);
    });
    connection.on("waiterCallUpdated", (payload) => {
      handlersRef.current.onWaiterCallUpdated?.(payload);
    });
    connection.on("whatsappInboxUpdated", (payload) => {
      handlersRef.current.onWhatsappInboxUpdated?.(payload);
    });
    connection.on("courierGpsUpdated", (payload) => {
      handlersRef.current.onCourierGpsUpdated?.(payload);
    });

    connection.onreconnecting(() => {
      if (!cancelled) setConnState("reconnecting");
    });
    connection.onreconnected(() => {
      if (!cancelled) setConnState("connected");
    });
    connection.onclose(() => {
      if (!cancelled) setConnState("disconnected");
    });

    connection
      .start()
      .then(() => {
        if (!cancelled) setConnState("connected");
      })
      .catch(() => {
        if (!cancelled) setConnState("disconnected");
      });

    return () => {
      cancelled = true;
      void connection.stop().catch(() => {});
    };
  }, [accessToken]);

  return connState;
}
