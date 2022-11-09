import apiClient from "../http-common";

class BaseHttpService {
    getData<T>(url: string): Promise<T> {
        return new Promise((resolve, reject) => {
            apiClient.get(url).then((response) => {
                resolve(response.data);
            }).catch((reason) => {
                reject(reason);
            });
        })
    }

    postData<T>(url: string, data?: any): Promise<T> {
        return new Promise((resolve, reject) => {
            apiClient.post(url, data).then((response) => {
                resolve(response.data);
            }).catch((reason) => {
                reject(reason);
            })
        })
    }

    putData<T>(url: string, data?: any): Promise<T> {
        return new Promise((resolve, reject) => {
            apiClient.put(url, data).then((response) => {
                resolve(response.data);
            }).catch((reason) => {
                reject(reason);
            })
        })
    }

    delete(url: string): Promise<void> {
        return new Promise((resolve, reject) => {
            apiClient.delete(url).then((response) => {
                resolve();
            }).catch((reason) => {
                reject(reason);
            })
        })
    }
}

export default BaseHttpService;
