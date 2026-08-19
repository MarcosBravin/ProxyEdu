import { defineConfig } from "astro/config";
import react from "@astrojs/react";
import sitemap from "@astrojs/sitemap";

export default defineConfig({
  site: "https://proxyedu.bravintech.com",
  output: "static",
  trailingSlash: "always",
  integrations: [react(), sitemap({ customPages: ["https://proxyedu.bravintech.com/"] })],
});
