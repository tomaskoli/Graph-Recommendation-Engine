/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { SearchResultDto } from '../models/SearchResultDto';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class SearchService {
    /**
     * @returns SearchResultDto OK
     * @throws ApiError
     */
    public static globalSearch({
        q,
        limit,
    }: {
        q: string,
        limit: number | string,
    }): CancelablePromise<SearchResultDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/search',
            query: {
                'q': q,
                'limit': limit,
            },
            errors: {
                400: `Bad Request`,
            },
        });
    }
}
