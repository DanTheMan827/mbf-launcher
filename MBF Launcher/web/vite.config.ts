import { defineConfig } from 'vite';
import path from 'path';

export default defineConfig({
    build: {
        lib: {
            entry: path.resolve(__dirname, 'src/virtual_adb_test.ts'),
            name: 'MbfVirtualAdbTest',
            fileName: () => 'virtual_adb_test.js',
            formats: ['iife'],
        },
        outDir: path.resolve(__dirname, '../Resources/Raw'),
        emptyOutDir: false,
        copyPublicDir: false,
        minify: false,
    },
});
