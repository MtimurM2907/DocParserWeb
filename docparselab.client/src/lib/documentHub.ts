import { HubConnectionBuilder } from '@microsoft/signalr';

export function createDocumentHubConnection(token: string) {
  return new HubConnectionBuilder()
    .withUrl(`/hubs/document?access_token=${encodeURIComponent(token)}`)
    .withAutomaticReconnect()
    .build();
}
