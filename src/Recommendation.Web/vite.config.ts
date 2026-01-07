import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  
  return {
    plugins: [react()],
    server: {
      port: parseInt(env.VITE_PORT || '5173'),
      proxy: {
        '/api': {
          target: env.services__recommendation_api__https__0 || 'http://localhost:5188',
          changeOrigin: true,
        },
      },
    },
  };
})
