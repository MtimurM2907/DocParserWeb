import type { CadesPluginApi } from '../../types/cadesplugin';

const CADES_SCRIPT_CANDIDATES = [
  '/cadesplugin_api.js',
  'https://www.cryptopro.ru/sites/default/files/products/cades/cadesplugin_api.js',
];

export type CadesCertificateInfo = {
  thumbprint: string;
  subject: string;
  validTo: string;
};

let scriptLoadPromise: Promise<void> | null = null;

function loadScriptOnce(src: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const existing = document.querySelector<HTMLScriptElement>(`script[data-cades-src="${src}"]`);
    if (existing) {
      existing.addEventListener('load', () => resolve(), { once: true });
      existing.addEventListener('error', () => reject(new Error(`Не удалось загрузить ${src}`)), { once: true });
      if (existing.dataset.loaded === '1') resolve();
      return;
    }

    const script = document.createElement('script');
    script.src = src;
    script.charset = 'utf-8';
    script.async = true;
    script.dataset.cadesSrc = src;
    script.onload = () => {
      script.dataset.loaded = '1';
      resolve();
    };
    script.onerror = () => reject(new Error(`Не удалось загрузить ${src}`));
    document.head.appendChild(script);
  });
}

async function loadCadesScript(): Promise<void> {
  if (scriptLoadPromise) return scriptLoadPromise;

  scriptLoadPromise = (async () => {
    let lastError: Error | null = null;
    for (const src of CADES_SCRIPT_CANDIDATES) {
      try {
        await loadScriptOnce(src);
        return;
      } catch (e) {
        lastError = e instanceof Error ? e : new Error(String(e));
      }
    }
    throw lastError ?? new Error('Не удалось загрузить cadesplugin_api.js');
  })();

  return scriptLoadPromise;
}

export async function getCadesPlugin(): Promise<CadesPluginApi> {
  await loadCadesScript();
  const plugin = window.cadesplugin;
  if (!plugin) {
    throw new Error(
      'Плагин КриптоПро не найден. Установите «КриптоПро ЭЦП Browser plug-in» и расширение для вашего браузера, затем перезагрузите страницу.',
    );
  }

  await new Promise<void>((resolve, reject) => {
    plugin.then(
      () => resolve(),
      (err) => reject(err instanceof Error ? err : new Error(String(err))),
    );
  });

  return plugin;
}

export async function isCadesPluginAvailable(): Promise<boolean> {
  try {
    await getCadesPlugin();
    return true;
  } catch {
    return false;
  }
}

export async function listCadesCertificates(plugin: CadesPluginApi): Promise<CadesCertificateInfo[]> {
  const store = await plugin.CreateObjectAsync('CAdESCOM.Store');
  await (store as { Open: (a: number, b: string, c: number) => Promise<void> }).Open(
    plugin.CAPICOM_CURRENT_USER_STORE,
    plugin.CAPICOM_MY_STORE,
    plugin.CAPICOM_STORE_OPEN_MAXIMUM_ALLOWED,
  );

  const certs = await (store as { Certificates: Promise<CadesAsyncStoreCerts> }).Certificates;
  const count = await certs.Count;
  const result: CadesCertificateInfo[] = [];

  for (let i = 1; i <= count; i++) {
    const cert = await certs.Item(i);
    const subject = String(await cert.SubjectName);
    const thumbprint = String(await cert.Thumbprint).replace(/\s/g, '').toUpperCase();
    let validTo = '';
    try {
      validTo = String(await cert.ValidToDate);
    } catch {
      validTo = '';
    }
    result.push({ thumbprint, subject, validTo });
  }

  await (store as { Close: () => Promise<void> }).Close();
  return result;
}

type CadesAsyncStoreCerts = {
  Count: Promise<number>;
  Item: (index: number) => Promise<CadesAsyncCert>;
};

type CadesAsyncCert = {
  SubjectName: Promise<string>;
  Thumbprint: Promise<string>;
  ValidToDate?: Promise<string>;
};

export async function signDetachedCmsBase64(
  plugin: CadesPluginApi,
  contentBase64: string,
  certificateThumbprint: string,
): Promise<string> {
  const thumb = certificateThumbprint.replace(/\s/g, '').toUpperCase();

  const store = await plugin.CreateObjectAsync('CAdESCOM.Store');
  await (store as { Open: (a: number, b: string, c: number) => Promise<void> }).Open(
    plugin.CAPICOM_CURRENT_USER_STORE,
    plugin.CAPICOM_MY_STORE,
    plugin.CAPICOM_STORE_OPEN_MAXIMUM_ALLOWED,
  );

  const certs = await (store as { Certificates: Promise<CadesAsyncStoreCerts> }).Certificates;
  const found = await (
    certs as unknown as { Find: (findType: number, thumb: string) => Promise<CadesAsyncStoreCerts> }
  ).Find(plugin.CAPICOM_CERTIFICATE_FIND_SHA1_HASH, thumb);
  if ((await found.Count) < 1) {
    await (store as { Close: () => Promise<void> }).Close();
    throw new Error('Сертификат с выбранным отпечатком не найден в хранилище «Личные».');
  }

  const cert = await found.Item(1);
  const signer = await plugin.CreateObjectAsync('CAdESCOM.CPSigner');
  await (signer as { propset_Certificate: (c: CadesAsyncCert) => Promise<void> }).propset_Certificate(cert);

  const signedData = await plugin.CreateObjectAsync('CAdESCOM.CadesSignedData');
  await (
    signedData as { propset_ContentEncoding: (v: number) => Promise<void> }
  ).propset_ContentEncoding(plugin.CADESCOM_BASE64_TO_BINARY);
  await (signedData as { propset_Content: (v: string) => Promise<void> }).propset_Content(contentBase64);

  const signature = await (
    signedData as {
      SignCades: (s: unknown, type: number, detached: boolean) => Promise<string>;
    }
  ).SignCades(signer, plugin.CADESCOM_CADES_BES, true);

  await (store as { Close: () => Promise<void> }).Close();
  return signature;
}

export function cmsBase64ToSignatureFile(base64Signature: string, fileName = 'document.sig'): File {
  const normalized = base64Signature.replace(/\s/g, '');
  const binary = atob(normalized);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return new File([bytes], fileName, { type: 'application/pkcs7-signature' });
}
