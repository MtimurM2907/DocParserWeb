import { fileURLToPath, URL } from 'node:url';

import { defineConfig, type ConfigEnv, type UserConfig } from 'vite';
import plugin from '@vitejs/plugin-react';
import fs from 'fs';
import path from 'path';
import child_process from 'child_process';
import { env } from 'process';

// https://vitejs.dev/config/
export default defineConfig(({ command }: ConfigEnv): UserConfig => {
    const base: UserConfig = {
        plugins: [plugin()],
        resolve: {
            alias: {
                '@': fileURLToPath(new URL('./src', import.meta.url))
            }
        },
        build: {
            outDir: '../DocParseLab.Server/wwwroot',
            emptyOutDir: true,
        },
    };

    // Сертификаты нужны только для локальной разработки (vite dev) вместе с ASP.NET dev-certs.
    // В CI/Docker сборке (vite build) сертификаты не создаём и не читаем.
    if (command !== 'serve') {
        return base;
    }

    const baseFolder =
        env.APPDATA !== undefined && env.APPDATA !== ''
            ? `${env.APPDATA}/ASP.NET/https`
            : `${env.HOME}/.aspnet/https`;

    const certificateName = 'docparselab.client';
    const certFilePath = path.join(baseFolder, `${certificateName}.pem`);
    const keyFilePath = path.join(baseFolder, `${certificateName}.key`);

    if (!fs.existsSync(baseFolder)) {
        fs.mkdirSync(baseFolder, { recursive: true });
    }

    if (!fs.existsSync(certFilePath) || !fs.existsSync(keyFilePath)) {
        const status = child_process.spawnSync('dotnet', [
            'dev-certs',
            'https',
            '--export-path',
            certFilePath,
            '--format',
            'Pem',
            '--no-password',
        ], { stdio: 'inherit' }).status;
        if (status !== 0) {
            throw new Error('Could not create certificate.');
        }
    }

    const target = env.ASPNETCORE_HTTPS_PORT ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}` :
        env.ASPNETCORE_URLS ? env.ASPNETCORE_URLS.split(';')[0] : 'https://localhost:7017';

    return {
        ...base,
        server: {
            proxy: {
                '^/api': {
                    target,
                    secure: false
                }
            },
            port: parseInt(env.DEV_SERVER_PORT || '53671'),
            https: {
                key: fs.readFileSync(keyFilePath),
                cert: fs.readFileSync(certFilePath),
            }
        }
    };
});
