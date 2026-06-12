// vite.config.js
import { defineConfig, loadEnv } from "file:///D:/DevRepos/react-vite-admin-phase1/node_modules/vite/dist/node/index.js";
import react from "file:///D:/DevRepos/react-vite-admin-phase1/node_modules/@vitejs/plugin-react/dist/index.js";
var vite_config_default = defineConfig(function(_a) {
  var mode = _a.mode;
  var env = loadEnv(mode, ".", "");
  var proxyTarget = env.VITE_DEV_PROXY_TARGET || "http://localhost:5001";
  return {
    plugins: [react()],
    server: {
      proxy: {
        "/api": {
          target: proxyTarget,
          changeOrigin: true
        },
        "/hangfire": {
          target: proxyTarget,
          changeOrigin: true
        }
      }
    }
  };
});
export {
  vite_config_default as default
};
//# sourceMappingURL=data:application/json;base64,ewogICJ2ZXJzaW9uIjogMywKICAic291cmNlcyI6IFsidml0ZS5jb25maWcuanMiXSwKICAic291cmNlc0NvbnRlbnQiOiBbImNvbnN0IF9fdml0ZV9pbmplY3RlZF9vcmlnaW5hbF9kaXJuYW1lID0gXCJEOlxcXFxEZXZSZXBvc1xcXFxyZWFjdC12aXRlLWFkbWluLXBoYXNlMVwiO2NvbnN0IF9fdml0ZV9pbmplY3RlZF9vcmlnaW5hbF9maWxlbmFtZSA9IFwiRDpcXFxcRGV2UmVwb3NcXFxccmVhY3Qtdml0ZS1hZG1pbi1waGFzZTFcXFxcdml0ZS5jb25maWcuanNcIjtjb25zdCBfX3ZpdGVfaW5qZWN0ZWRfb3JpZ2luYWxfaW1wb3J0X21ldGFfdXJsID0gXCJmaWxlOi8vL0Q6L0RldlJlcG9zL3JlYWN0LXZpdGUtYWRtaW4tcGhhc2UxL3ZpdGUuY29uZmlnLmpzXCI7aW1wb3J0IHsgZGVmaW5lQ29uZmlnLCBsb2FkRW52IH0gZnJvbSAndml0ZSc7XG5pbXBvcnQgcmVhY3QgZnJvbSAnQHZpdGVqcy9wbHVnaW4tcmVhY3QnO1xuZXhwb3J0IGRlZmF1bHQgZGVmaW5lQ29uZmlnKGZ1bmN0aW9uIChfYSkge1xuICAgIHZhciBtb2RlID0gX2EubW9kZTtcbiAgICB2YXIgZW52ID0gbG9hZEVudihtb2RlLCAnLicsICcnKTtcbiAgICB2YXIgcHJveHlUYXJnZXQgPSBlbnYuVklURV9ERVZfUFJPWFlfVEFSR0VUIHx8ICdodHRwOi8vbG9jYWxob3N0OjUwMDEnO1xuICAgIHJldHVybiB7XG4gICAgICAgIHBsdWdpbnM6IFtyZWFjdCgpXSxcbiAgICAgICAgc2VydmVyOiB7XG4gICAgICAgICAgICBwcm94eToge1xuICAgICAgICAgICAgICAgICcvYXBpJzoge1xuICAgICAgICAgICAgICAgICAgICB0YXJnZXQ6IHByb3h5VGFyZ2V0LFxuICAgICAgICAgICAgICAgICAgICBjaGFuZ2VPcmlnaW46IHRydWUsXG4gICAgICAgICAgICAgICAgfSxcbiAgICAgICAgICAgICAgICAnL2hhbmdmaXJlJzoge1xuICAgICAgICAgICAgICAgICAgICB0YXJnZXQ6IHByb3h5VGFyZ2V0LFxuICAgICAgICAgICAgICAgICAgICBjaGFuZ2VPcmlnaW46IHRydWUsXG4gICAgICAgICAgICAgICAgfSxcbiAgICAgICAgICAgIH0sXG4gICAgICAgIH0sXG4gICAgfTtcbn0pO1xuIl0sCiAgIm1hcHBpbmdzIjogIjtBQUFpUyxTQUFTLGNBQWMsZUFBZTtBQUN2VSxPQUFPLFdBQVc7QUFDbEIsSUFBTyxzQkFBUSxhQUFhLFNBQVUsSUFBSTtBQUN0QyxNQUFJLE9BQU8sR0FBRztBQUNkLE1BQUksTUFBTSxRQUFRLE1BQU0sS0FBSyxFQUFFO0FBQy9CLE1BQUksY0FBYyxJQUFJLHlCQUF5QjtBQUMvQyxTQUFPO0FBQUEsSUFDSCxTQUFTLENBQUMsTUFBTSxDQUFDO0FBQUEsSUFDakIsUUFBUTtBQUFBLE1BQ0osT0FBTztBQUFBLFFBQ0gsUUFBUTtBQUFBLFVBQ0osUUFBUTtBQUFBLFVBQ1IsY0FBYztBQUFBLFFBQ2xCO0FBQUEsUUFDQSxhQUFhO0FBQUEsVUFDVCxRQUFRO0FBQUEsVUFDUixjQUFjO0FBQUEsUUFDbEI7QUFBQSxNQUNKO0FBQUEsSUFDSjtBQUFBLEVBQ0o7QUFDSixDQUFDOyIsCiAgIm5hbWVzIjogW10KfQo=
