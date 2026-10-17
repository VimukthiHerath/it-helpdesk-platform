FROM node:25-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm install
COPY . .
# Optional: turns on browser monitoring. It is not a secret for a browser, but it is kept out of git.
ARG REACT_APP_APPINSIGHTS_CONNECTION_STRING
ENV REACT_APP_APPINSIGHTS_CONNECTION_STRING=$REACT_APP_APPINSIGHTS_CONNECTION_STRING
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/build /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]