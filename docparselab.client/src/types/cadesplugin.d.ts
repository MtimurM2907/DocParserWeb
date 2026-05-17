/** CryptoPro CAdES Browser plug-in (cadesplugin_api.js) */
export interface CadesPluginApi {
  then(onfulfilled?: () => void, onrejected?: (reason: unknown) => void): Promise<void>;
  CreateObjectAsync(name: string): Promise<CadesAsyncObject>;
  CADESCOM_CADES_BES: number;
  CADESCOM_BASE64_TO_BINARY: number;
  CAPICOM_CURRENT_USER_STORE: number;
  CAPICOM_MY_STORE: string;
  CAPICOM_STORE_OPEN_MAXIMUM_ALLOWED: number;
  CAPICOM_CERTIFICATE_FIND_SHA1_HASH: number;
  async_spawn(generator: Generator<Promise<unknown>, unknown, unknown>): Promise<unknown>;
}

export interface CadesAsyncObject {
  [key: string]: unknown;
}

declare global {
  interface Window {
    cadesplugin?: CadesPluginApi;
  }
}

export {};
