import * as signalR from '@microsoft/signalr'

// Minimal SignalR round-trip against the placeholder PingHub, proving the live-event
// pipeline works end to end before any real hub (inspector/run progress) exists.
export async function pingHub(): Promise<string> {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/ping')
    .build()

  try {
    await connection.start()
    return await connection.invoke<string>('Ping')
  } finally {
    await connection.stop()
  }
}
