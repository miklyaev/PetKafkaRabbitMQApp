import { useEffect, useRef, useState } from 'react';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import type { NotificationPayload } from './types';

/** Подключается к /hubs/notifications; возвращает true, когда канал SignalR жив. */
export function useNotificationHub(onNotification: (n: NotificationPayload) => void): boolean {
  const [connected, setConnected] = useState(false);
  const callbackRef = useRef(onNotification);
  callbackRef.current = onNotification;

  useEffect(() => {
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl('/hubs/notifications')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('NotificationReceived', (n: NotificationPayload) => callbackRef.current(n));
    connection.onreconnecting(() => setConnected(false));
    connection.onreconnected(() => setConnected(true));
    connection.onclose(() => setConnected(false));

    connection.start().then(() => setConnected(true)).catch(() => setConnected(false));

    return () => {
      void connection.stop();
    };
  }, []);

  return connected;
}
