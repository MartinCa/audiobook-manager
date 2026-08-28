import apiClient from "../http-common";

// Thin typed wrapper over axios. Deliberately not wrapping the axios promise in `new Promise`:
// that indirection swallowed synchronous throws and added a microtask hop per request without
// changing behaviour otherwise.
class BaseHttpService {
  async getData<T>(url: string): Promise<T> {
    const response = await apiClient.get<T>(url);
    return response.data;
  }

  async postData<T>(url: string, data?: any): Promise<T> {
    const response = await apiClient.post<T>(url, data);
    return response.data;
  }

  async putData<T>(url: string, data?: any): Promise<T> {
    const response = await apiClient.put<T>(url, data);
    return response.data;
  }

  async delete(url: string): Promise<void> {
    await apiClient.delete(url);
  }
}

export default BaseHttpService;
