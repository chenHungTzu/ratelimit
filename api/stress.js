import http from 'k6/http';
import { sleep ,check} from 'k6';

export let options = {
    vus: 2, // 這邊固定負載，分別以1、2個vu 來測試
    duration: '1m', // 持續時間一分鐘
};
export default function () {
    const params = {
        headers: {
            'X-Real-IP': '8.8.8.8', // 模擬來源為8.8.8.8
        },
    };
    // 這邊是要測試的API
    let response = http.get('http://localhost:55003/WeatherForecast', params); 

    console.log(response.status);

    // 驗證結果
    check(response, {
        'is status 200': (r) => r.status === 200,
      });

    // 間隔1秒
    sleep(1); 
}